using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ReunionLocale;
public static class V2Checks
{
 public static async Task Run(string work,Action<bool,string> check,bool playback)
 {
  string runtime=await RuntimeBootstrap.Ensure();check(File.Exists(runtime),"Extraction du moteur embarqué et empreintes vérifiées");
  string license=Path.Combine(RuntimeBootstrap.Home,"share","licenses","nemo-speech","LICENSE");
  string original=await File.ReadAllTextAsync(license);await File.WriteAllTextAsync(license,"corrompu");await RuntimeBootstrap.Ensure();check(await File.ReadAllTextAsync(license)==original,"Réparation automatique d’un composant extrait corrompu");
  var source=Path.Combine(work,"test-models",Engine.Asr.File);var stamp=File.GetLastWriteTimeUtc(source);
  await ModelManager.Migrate([Path.Combine(work,"test-models")],(_,_)=>{},CancellationToken.None);
  check(await ModelManager.IsInstalled(ModelCatalog.Get("nemotron"),CancellationToken.None),"Migration et intégrité des modèles Nemotron existants");
  check(stamp==File.GetLastWriteTimeUtc(source),"Les modèles originaux ne sont pas modifiés par la migration");
  check(await ModelManager.IsInstalled(ModelCatalog.Get("parakeet"),CancellationToken.None),"Modèle Parakeet officiel importé et vérifié");
  string invalid=Path.Combine(work,"invalid.gguf");await File.WriteAllTextAsync(invalid,"GGUF invalide");
  try{await ModelManager.Import(ModelCatalog.Get("nemotron"),[invalid],(_,_)=>{},CancellationToken.None);check(false,"Import corrompu refusé");}catch(InvalidDataException){check(await Engine.Verify(Engine.Asr,CancellationToken.None),"Import corrompu refusé sans altérer le modèle valide");}
  check(!ModelCatalog.Get("transcribe30b").Supported,"30B indisponible non activable");
  byte[] content=new byte[2*1024*1024+73];new Random(42).NextBytes(content);
  var fixture=new ModelFile("fixture","revision","resume-test-"+Guid.NewGuid().ToString("N")+".gguf",content.Length,Convert.ToHexString(SHA256.HashData(content)));
  // A loopback-free HTTP test handler exercises actual HTTP range/resume code.
  using var handler=new RangeHandler(content);using var http=new HttpClient(handler);
  var profile=new ModelProfile("fixture","Test","",fixture,true);
  using(var cancellation=new CancellationTokenSource()){
   try{await ModelManager.Install(profile,(_,_)=>{if(File.Exists(fixture.Local+".part")&&new FileInfo(fixture.Local+".part").Length>0)cancellation.Cancel();},cancellation.Token,http);check(false,"Téléchargement interrompu");}catch(OperationCanceledException){check(File.Exists(fixture.Local+".part"),"Annulation conserve le téléchargement partiel");}
  }
  await ModelManager.Install(profile,(_,_)=>{},CancellationToken.None,http);
  check(handler.LastRange>0&&await Engine.Verify(fixture,CancellationToken.None),"Reprise HTTP Range et vérification SHA-256 avant installation");
  var points=new List<AnalysisProgress>();var tracker=new ProgressTracker(points.Add);
  foreach(double p in new[]{0d,25,12,78,99,100})tracker.Update("Test",p);
  check(points.Zip(points.Skip(1)).All(p=>p.First.Percent<=p.Second.Percent)&&points.All(p=>p.Percent<100),"Progression monotone et 100 % réservé à la sauvegarde réussie");
  using(var reader=new AudioFileReader(Path.Combine(work,"french.wav"))){
   var bounded=AudioPlayback.CreateBounded(reader,1.2,2.6,reader.TotalTime.TotalSeconds);float[] buffer=new float[4096];int read;long count=0;while((read=bounded.Read(buffer,0,buffer.Length))>0)count+=read;
   check(Math.Abs((double)count/bounded.WaveFormat.SampleRate/bounded.WaveFormat.Channels-1.4)<.01,"Lecture bornée de 1,4 s indépendante de la mise en mémoire tampon");
  }
  string folder=Path.Combine(work,"v2-pipeline");Directory.CreateDirectory(folder);File.Copy(Path.Combine(work,"multi.wav"),Path.Combine(folder,"pc.wav"),true);File.Copy(Path.Combine(work,"french.wav"),Path.Combine(folder,"mic.wav"),true);
  var meeting=new Meeting{Directory=folder};var settings=new Settings{Engine=runtime,Language=2};
  var actual=new List<AnalysisProgress>();var segments=await Engine.TranscribeDetailed(meeting,settings,p=>actual.Add(p),CancellationToken.None);
  check(segments.Any(s=>s.Track=="mic")&&segments.Where(s=>s.Track=="pc").Select(s=>s.VoiceId).Distinct().Count()>=2,"Worker natif : deux pistes et identifiants de voix continus");
  check(actual.Count>30&&actual.Zip(actual.Skip(1)).All(p=>p.First.Percent<=p.Second.Percent)&&actual.All(p=>p.Percent<100),"Progression native liée aux échantillons effectivement traités");
  using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(2))){try{await Engine.TranscribeDetailed(meeting,settings,_=>{},cancel.Token);check(false,"Annulation du worker");}catch(OperationCanceledException){check(File.Exists(Path.Combine(folder,"pc.wav")),"Annulation du worker conserve les pistes audio");}}
  settings.ModelId="parakeet";
  var alternative=await Engine.TranscribeDetailed(meeting,settings,_=>{},CancellationToken.None);
  check(alternative.Any(s=>s.Track=="mic")&&alternative.Any(s=>s.Track=="pc"),"Parakeet : traitement hors streaming avec intervenants");
  if(playback){
   using var en=new MMDeviceEnumerator();using var device=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
   using var loopback=new WasapiLoopbackCapture(device);float peak=0;loopback.DataAvailable+=(_,e)=>{for(int i=0;i+4<=e.BytesRecorded;i+=4)peak=Math.Max(peak,Math.Abs(BitConverter.ToSingle(e.Buffer,i)));};loopback.StartRecording();
   using var sound=new AudioPlayback();using var done=new ManualResetEventSlim();Exception? failure=null;
   sound.Stopped+=error=>{failure=error;done.Set();};var clock=Stopwatch.StartNew();sound.Play(Path.Combine(work,"french.wav"),1.2,2.6,device.ID);
   bool ended=await Task.Run(()=>done.Wait(TimeSpan.FromSeconds(6)));loopback.StopRecording();
   check(ended&&failure==null&&clock.Elapsed.TotalSeconds>=1.2&&peak>.005,"Lecture WASAPI réelle sur la sortie sélectionnée, signal capturé et durée respectée");
  }
 }
 sealed class RangeHandler(byte[] content) : HttpMessageHandler
 {
  public long LastRange;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();long offset=request.Headers.Range?.Ranges.First().From??0;LastRange=offset;var response=new HttpResponseMessage(offset>0?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new ByteArrayContent(content,(int)offset,content.Length-(int)offset)};if(offset>0)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(offset,content.Length-1,content.Length);return Task.FromResult(response);}
 }
}
