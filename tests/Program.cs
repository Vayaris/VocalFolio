using System.IO;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ReunionLocale;
if(args.Length==2&&args[0]=="--bootstrap"){Environment.SetEnvironmentVariable("REUNION_LOCALE_DATA",args[1]);Console.WriteLine(await RuntimeBootstrap.Ensure());return;}
if(args.Length==2&&args[0]=="--worker"){try{NativeWorker.Run(args[1],Console.WriteLine);}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}return;}
var basePath=Path.GetFullPath(args[0]);
if(args.Contains("--v2"))Environment.SetEnvironmentVariable("REUNION_LOCALE_DATA",Path.Combine(basePath,"v2-profile"));
int assertions=0;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("OK · "+label);assertions++;}
using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(basePath,"real-result.json"))))
{
 var segments=Engine.Parse(doc.RootElement,"pc");
 Check(segments.Count>0&&segments.All(s=>s.VoiceId=="pc-1"),"Lecture des mots et voix du résultat NVIDIA réel");
 Check(segments.All(s=>s.End>=s.Start),"Horodatages cohérents");
 Check(Engine.Parse(doc.RootElement,"mic").All(s=>s.Speaker=="Moi"),"Attribution indépendante du microphone");
}
using(var doc=JsonDocument.Parse("""{"words":[{"word":"Bonjour","start":0,"end":1,"speaker":1},{"word":"Salut","start":1,"end":2,"speaker":2},{"word":"Oui","start":3,"end":4,"speaker":1},{"word":"?","start":4,"end":5}]}"""))
{
 var segments=Engine.Parse(doc.RootElement,"pc");
 Check(segments.Select(s=>s.VoiceId).SequenceEqual(new[]{"pc-1","pc-2","pc-1","unknown"}),"Identifiants stables et voix inconnue explicite");
 var m=new Meeting{Directory=Path.Combine(basePath,"store-check"),Segments=segments,Title="Essai accents éèà"};
 foreach(var s in m.Segments.Where(s=>s.VoiceId=="pc-1"))s.Speaker="Camille";
 Store.Save(m);var saved=Store.Read<Meeting>(Path.Combine(m.Directory,"meeting.json"))!;
 Check(saved.Segments.Count(s=>s.Speaker=="Camille")==2,"Persistance des noms et du texte UTF-8");
 Check(Store.Export(m,true).Contains("00:00:01,000 --> 00:00:02,000")&&Store.Export(m,true).Contains("Camille : Bonjour"),"Export SRT horodaté avec noms");
 Check(Store.Export(m,false).Contains("[00:00:03] Camille : Oui"),"Export TXT avec noms");
}
using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(basePath,"multi-result.json"))))
{
 var segments=Engine.Parse(doc.RootElement,"pc");
 var ids=segments.Select(s=>s.VoiceId).Distinct().ToList();
 Check(ids.Count>=2,"Plusieurs intervenants détectés dans l’échantillon public AMI : "+string.Join(", ",ids));
}
if(args.Contains("--capture"))
{
 using var en=new MMDeviceEnumerator();using var input=en.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Multimedia);using var output=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
 string dir=Path.Combine(basePath,"capture-check");Directory.CreateDirectory(dir);
 double duration;
 using(var capture=new AudioSession(input.ID,output.ID,dir)) {
  await Task.Delay(1200);capture.Pause(true);double before=capture.Seconds;await Task.Delay(500);Check(Math.Abs(capture.Seconds-before)<.02,"Pause sans progression de la chronologie");capture.Pause(false);await Task.Delay(1200);duration=capture.Seconds;
 }
 using var mic=new WaveFileReader(Path.Combine(dir,"mic.wav"));using var pc=new WaveFileReader(Path.Combine(dir,"pc.wav"));
 Check(Math.Abs(mic.TotalTime.TotalSeconds-pc.TotalTime.TotalSeconds)<.15,"Synchronisation des pistes micro et sortie");
 Check(Math.Abs(mic.TotalTime.TotalSeconds-duration)<.2,"Durée audio cohérente après pause/reprise");
}
using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(basePath,"french-result.json"))))
 Check(string.Join(" ",Engine.Parse(doc.RootElement,"mic").Select(s=>s.Text)).Equals("Je souhaite changer mon adresse",StringComparison.OrdinalIgnoreCase),"Transcription française conforme à la référence MINDS-14");
if(args.Contains("--pipeline"))
{
 string folder=Path.Combine(basePath,"pipeline-check");Directory.CreateDirectory(folder);
 File.Copy(Path.Combine(basePath,"multi.wav"),Path.Combine(folder,"pc.wav"),true);
 File.Copy(Path.Combine(basePath,"french.wav"),Path.Combine(folder,"mic.wav"),true);
 var meeting=new Meeting{Directory=folder};
 var engine=Path.Combine(basePath,"nemo","nemo-speech-nightly-windows-x86_64-cpu","bin","nemo-speech.exe");
 var segments=await Engine.Transcribe(meeting,new Settings{Engine=engine,Language=2},(s,p)=>Console.WriteLine(s),CancellationToken.None);
 Check(segments.Any(s=>s.Track=="mic")&&segments.Any(s=>s.Track=="pc"),"Pipeline applicatif complet : deux pistes analysées et fusionnées");
 using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(3));
 try {await Engine.Transcribe(meeting,new Settings{Engine=engine},(_,_)=>{},cancel.Token);throw new Exception("Annulation ignorée");}catch(OperationCanceledException){Check(true,"Annulation propagée par le pipeline");}
}
if(args.Contains("--v2"))await V2Checks.Run(basePath,Check,args.Contains("--playback"));
Console.WriteLine($"{assertions} contrôles réussis.");


