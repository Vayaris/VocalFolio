using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using NAudio.Wave;
using System.Security.Cryptography;
using System.Text.Json;
namespace ReunionLocale;
public sealed record ModelFile(string Repo,string Revision,string File,long Size,string Sha)
{
 public string Local => Path.Combine(Store.Models,File);
 public string Url=>$"https://huggingface.co/{Repo}/resolve/{Revision}/{File}";
}
public static class Engine
{
 public static readonly ModelFile Asr=new("nvidia/nemotron-3.5-asr-streaming-0.6b","1c8deaecc64b91f034d73e08dd8b64625eb3395d","nemotron-3.5-asr-streaming-0.6b.q8_0.gguf",741548352,"a5c435f294eea8f88ce68dd27b8c3bfea7f777cb2fbba04fcd30eaa555f429ae");
 public static readonly ModelFile Diar=new("nvidia/diar_streaming_sortformer_4spk-v2","5240a64075176943f677d30fa2171c780229f341","diar_streaming_sortformer_4spk-v2.q8_0.gguf",147075776,"0679cfeb1ce356d0dea9470b31274f4bfc7eb927497d82005483770666da998a");
 public static async Task<bool> Verify(ModelFile m,CancellationToken ct)
  =>await VerifyPath(m,m.Local,ct);
 public static async Task<bool> VerifyPath(ModelFile m,string path,CancellationToken ct)
 {
  if(!File.Exists(path)||new FileInfo(path).Length!=m.Size)return false;
  await using var f=File.OpenRead(path);
  return Convert.ToHexString(await SHA256.HashDataAsync(f,ct)).Equals(m.Sha,StringComparison.OrdinalIgnoreCase);
 }
 public static Task Download(Action<string,double> progress,CancellationToken ct)=>ModelManager.Install(ModelCatalog.Get("nemotron"),progress,ct);
 public static async Task<List<Segment>> Transcribe(Meeting m,Settings settings,Action<string,double> progress,CancellationToken ct)
  =>await TranscribeDetailed(m,settings,p=>progress(p.Display,p.Percent),ct);
 public static async Task<List<Segment>> TranscribeDetailed(Meeting m,Settings settings,Action<AnalysisProgress> report,CancellationToken ct)
 {
  if(!File.Exists(settings.Engine))throw new FileNotFoundException("Moteur NVIDIA introuvable. Vérifiez son chemin dans Paramètres.");
  var profile=ModelCatalog.Get(settings.ModelId);if(!profile.Supported)throw new InvalidDataException("Ce profil n’est pas encore compatible.");
  var progress=new ProgressTracker(report);progress.Update("Vérification des modèles locaux",0);
  int verified=0;
  foreach(var model in profile.Files){if(!await Verify(model,ct))throw new InvalidDataException("Installez les modèles vérifiés depuis Paramètres avant l’analyse.");progress.Update("Vérification des modèles locaux",4d*++verified/profile.Files.Count);}
  double Duration(string track){using var wav=new WaveFileReader(Path.Combine(m.Directory,track+".wav"));return wav.TotalTime.TotalSeconds;}
  double total=Duration("pc")+Duration("mic"),completed=0;
  if(total<=0)throw new InvalidDataException("Les pistes audio sont vides.");
  var result=new List<Segment>();
  foreach(var track in new[]{"pc","mic"}) {
   var input=Path.Combine(m.Directory,track+".wav");
   if(!File.Exists(input))throw new FileNotFoundException("Piste audio absente : "+input);
   var output=Path.Combine(m.Directory,track+".json");
   double duration=Duration(track);string stage=track=="pc"?"Son du PC et intervenants":"Microphone";
   progress.Update("Chargement · "+stage,5+90*completed/total);
   string log=Path.Combine(m.Directory,track+"-engine.log");
   if(profile.Streaming){
    string request=Path.Combine(m.Directory,track+"-request.json");
    Store.Write(request,new WorkerRequest(input,output,settings.Engine,profile.Asr!.Local,track=="pc"?Diar.Local:null,settings.Language==2?"":settings.Language==0?"fr":"en",settings.Cuda));
    string exe=Environment.ProcessPath!;var args=new List<string>();
    if(Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))args.Add(Path.Combine(AppContext.BaseDirectory,"VocalFolio.dll"));
    args.AddRange(["--worker",request]);
    await Run(exe,args,log,ct,line=>{
     try{using var message=JsonDocument.Parse(line);if(message.RootElement.TryGetProperty("processed",out var processed))progress.Update(stage,5+90*(completed+Math.Min(duration,processed.GetDouble()))/total);}catch(JsonException){}
    });
   } else {
    var args=new List<string>{"transcribe",input,"--model",profile.Asr!.Local,"--device",settings.Cuda?"cuda:0":"cpu","--format","json","--output",output,"--force"};
    if(settings.Language!=2){args.Add("--language");args.Add(settings.Language==0?"fr":"en");}
    if(track=="pc"){args.Add("--diar-model");args.Add(Diar.Local);}
    string calibration=Path.Combine(Store.Root,"timings",profile.Id+(settings.Cuda?"-cuda":"-cpu")+"-"+track+".json");
    double factor=Store.Read<double?>(calibration)??0;var elapsed=Stopwatch.StartNew();
    await Run(settings.Engine,args,log,ct,null,()=>{
     double fraction=factor>0?Math.Min(.95,elapsed.Elapsed.TotalSeconds/Math.Max(1,duration*factor)):0;
     progress.Update(stage,5+90*(completed+duration*fraction)/total,true);
    });
    Store.Write(calibration,elapsed.Elapsed.TotalSeconds/Math.Max(1,duration));
   }
   using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(output,ct));
   result.AddRange(Parse(doc.RootElement,track));
   completed+=duration;progress.Update(stage,5+90*completed/total,!profile.Streaming);
  }
  foreach(var a in result)if(result.Any(b=>b.Track!=a.Track&&b.Start<a.End&&b.End>a.Start))a.Warning="Chevauchement";
  progress.Update("Préparation de la sauvegarde",98);return result.OrderBy(s=>s.Start).ToList();
 }
 public static List<Segment> Parse(JsonElement root,string track)
 {
  // The CLI emits word start/end in seconds and 1-based speaker IDs.
  if(root.TryGetProperty("result",out var nested))root=nested;
  if(!root.TryGetProperty("words",out var words)||words.ValueKind!=JsonValueKind.Array)
   throw new InvalidDataException("Le moteur n’a pas fourni de mots horodatés. Consultez le journal ; la transcription précédente est conservée.");
  var result=new List<Segment>();Segment? current=null;
  foreach(var w in words.EnumerateArray()) {
   string text=w.TryGetProperty("word",out var word)?word.GetString()??"":w.TryGetProperty("text",out word)?word.GetString()??"":"";
   if(string.IsNullOrWhiteSpace(text))continue;
   double start=w.GetProperty("start").GetDouble(),end=w.GetProperty("end").GetDouble();
   int speaker=w.TryGetProperty("speaker",out var sp)&&sp.TryGetInt32(out var id)?id:0;
   string voice=track=="mic"?"me":speaker>0?$"pc-{speaker}":"unknown";
   if(current==null||current.VoiceId!=voice||start-current.End>1.2||end-current.Start>12){current=new Segment{Start=start,End=end,VoiceId=voice,Speaker=track=="mic"?"Moi":speaker>0?$"Intervenant {speaker}":"Voix à attribuer",Track=track,Warning=voice=="unknown"?"Voix incertaine":""};result.Add(current);}
   current.Text+=(current.Text.Length>0?" ":"")+text.Trim();current.End=end;
  }
  return result;
 }
 static async Task Run(string exe,IEnumerable<string> args,string log,CancellationToken ct,Action<string>? outputLine=null,Action? tick=null)
 {
  var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(exe)!};
  foreach(var a in args)info.ArgumentList.Add(a);
  info.Environment["HF_HUB_OFFLINE"]="1";info.Environment["NO_COLOR"]="1";
  var elapsed=Stopwatch.StartNew();
  using var process=Process.Start(info)??throw new IOException("Impossible de lancer le moteur.");
  using var registration=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
  async Task<string> ReadOutput(){var text=new StringBuilder();while(await process.StandardOutput.ReadLineAsync(ct) is {} line){text.AppendLine(line);outputLine?.Invoke(line);}return text.ToString();}
  var stdout=ReadOutput();var stderr=process.StandardError.ReadToEndAsync(ct);
  long peak=0;
  try {
   while(!process.HasExited){try{peak=Math.Max(peak,process.PeakWorkingSet64);}catch(InvalidOperationException){}tick?.Invoke();await Task.Delay(250,ct);process.Refresh();}
   await process.WaitForExitAsync(ct);elapsed.Stop();await File.WriteAllTextAsync(log,await stderr+"\n"+await stdout+$"\nDurée de traitement : {elapsed.Elapsed.TotalSeconds:F2} s\nPic mémoire observé du processus : {peak/1048576d:F1} Mio\n",ct);ct.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new IOException($"Le moteur a échoué (code {process.ExitCode}). Journal : {log}");
  }
  finally {if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}try{await Task.WhenAll(stdout,stderr);}catch(OperationCanceledException){}}
 }
}

