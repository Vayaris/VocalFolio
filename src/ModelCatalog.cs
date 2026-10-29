using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
namespace ReunionLocale;
public sealed record ModelProfile(string Id,string Name,string Description,ModelFile? Asr,bool Streaming,bool Supported=true)
{
 public bool Verified { get; set; }
 public IReadOnlyList<ModelFile> Files=>Asr is null?[]:[Asr,Engine.Diar];
 public string State=>!Supported?"Indisponible — fichiers officiels et compatibilité non validés":Verified?"Installé":Files.All(f=>File.Exists(f.Local)&&new FileInfo(f.Local).Length==f.Size)?"Présent — vérification à l’utilisation":"Disponible au téléchargement";
 public string Label=>$"{Name} · {State}";
 public string Details=>$"{Description}\n"+(Supported?$"Transcription : {Asr!.Size/1_000_000d:F0} Mo · Voix : 147 Mo · CPU, environ 1 à 2 Go de RAM observés pour le modèle compact.\n{State}":"30B : plusieurs dizaines de Go de mémoire selon la quantification. Accès GGUF et configuration locale non validés ; téléchargement et activation désactivés.");
}
public static class ModelCatalog
{
 public static readonly ModelFile Parakeet=new("nvidia/parakeet-tdt-0.6b-v3","541d1f99c6b0c3cd0b11a95167540bb8edefd82b","parakeet-tdt-0.6b-v3.q8_0.gguf",713975456,"e3880d0aaaaf2c308ea2c35016b2b895c423eb3fda924c1b463d1c19b7f4d32e");
 public static readonly IReadOnlyList<ModelProfile> All=[
  new("nemotron","Nemotron ASR 0.6B — rapide","Transcription multilingue, français et anglais · traitement continu. Licence OpenMDW 1.1.",Engine.Asr,true),
  new("parakeet","Parakeet TDT 0.6B v3 — alternative","Transcription en 25 langues dont français et anglais · traitement par fichier. Licence CC BY 4.0. La qualité varie selon les réunions.",Parakeet,false),
  new("transcribe30b","Nemotron 3.5 Transcribe 30B — avancé","Grand modèle de transcription contextuelle NVIDIA · catalogue avancé.",null,false,false)
 ];
 public static ModelProfile Get(string id)=>All.FirstOrDefault(p=>p.Id==id)??All[0];
}
public static class ModelManager
{
 public static async Task<bool> IsInstalled(ModelProfile profile,CancellationToken ct)
 {
  if(!profile.Supported)return false;
  foreach(var file in profile.Files)if(!await Engine.Verify(file,ct)){profile.Verified=false;return false;}
  profile.Verified=true;return true;
 }
 public static async Task Migrate(IEnumerable<string> legacyFolders,Action<string,double> report,CancellationToken ct)
 {
  Directory.CreateDirectory(Store.Models);
  foreach(var file in ModelCatalog.All.SelectMany(p=>p.Files).DistinctBy(f=>f.Sha)){
   if(await Engine.Verify(file,ct))continue;
   foreach(var folder in legacyFolders.Distinct(StringComparer.OrdinalIgnoreCase)){
    var source=Path.Combine(folder,file.File);if(!File.Exists(source)||Path.GetFullPath(source).Equals(Path.GetFullPath(file.Local),StringComparison.OrdinalIgnoreCase))continue;
    report("Réutilisation d’un modèle existant · "+file.File,0);
    if(await Engine.VerifyPath(file,source,ct)){await CopyVerified(file,source,ct);break;}
   }
  }
 }
 static async Task CopyVerified(ModelFile file,string source,CancellationToken ct)
 {
  Directory.CreateDirectory(Store.Models);
  if(Path.GetFullPath(source).Equals(Path.GetFullPath(file.Local),StringComparison.OrdinalIgnoreCase))return;
  var temp=file.Local+".import";
  await using(var input=File.OpenRead(source))await using(var output=File.Create(temp))await input.CopyToAsync(output,ct);
  if(!await Engine.VerifyPath(file,temp,ct))throw new InvalidDataException("Empreinte incorrecte : "+file.File);
  File.Move(temp,file.Local,true);
 }
 public static async Task Import(ModelProfile profile,string[] sources,Action<string,double> report,CancellationToken ct)
 {
  if(!profile.Supported)throw new InvalidDataException("Cette architecture n’est pas encore prise en charge. Le 30B requiert notamment un décodeur et un encodeur compatibles, non validés ici.");
  var accepted=new List<(ModelFile File,string Source)>();
  foreach(var source in sources){
   ModelFile? match=null;
   foreach(var file in profile.Files)if(await Engine.VerifyPath(file,source,ct)){match=file;break;}
   if(match is null)throw new InvalidDataException("Fichier incompatible ou corrompu : "+Path.GetFileName(source)+". Sélectionnez les GGUF Q8 officiels du profil choisi ; les autres architectures/quantifications ne sont pas prises en charge.");
   accepted.Add((match,source));
  }
  if(!accepted.Any(x=>x.File==profile.Asr)&&!await Engine.Verify(profile.Asr!,ct))throw new InvalidDataException("Fichier de transcription manquant : "+profile.Asr!.File);
  foreach(var item in accepted){report("Import vérifié · "+item.File.File,0);await CopyVerified(item.File,item.Source,ct);}
  // Only companion files missing from this selected profile are fetched.
  await Install(profile,report,ct);
 }
 public static async Task Install(ModelProfile profile,Action<string,double> report,CancellationToken ct,HttpClient? http=null)
 {
  if(!profile.Supported)throw new InvalidDataException("Téléchargement indisponible : ce modèle n’a pas encore de distribution validée.");
  Directory.CreateDirectory(Store.Models);
  await using var lease=new FileStream(Path.Combine(Store.Models,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  using var owned=http==null?new HttpClient{Timeout=Timeout.InfiniteTimeSpan}:null;var client=http??owned!;
  long total=profile.Files.Sum(f=>f.Size),done=0;
  foreach(var file in profile.Files){
   report("Vérification · "+file.File,100d*done/total);
   if(await Engine.Verify(file,ct)){done+=file.Size;continue;}
   string temp=file.Local+".part";long offset=File.Exists(temp)?new FileInfo(temp).Length:0;
   if(offset>file.Size){File.Delete(temp);offset=0;}
   if(offset<file.Size){
    using var request=new HttpRequestMessage(HttpMethod.Get,file.Url);if(offset>0)request.Headers.Range=new RangeHeaderValue(offset,null);
    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
    bool resume=response.StatusCode==HttpStatusCode.PartialContent;
    if(resume&&response.Content.Headers.ContentRange?.From!=offset)throw new InvalidDataException("Réponse de reprise de téléchargement invalide.");
    if(!resume)offset=0;
    await using var input=await response.Content.ReadAsStreamAsync(ct);
    await using var output=new FileStream(temp,resume?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.Read,1024*1024,true);
    byte[] buffer=new byte[1024*1024];int read;
    while((read=await input.ReadAsync(buffer,ct))>0){await output.WriteAsync(buffer.AsMemory(0,read),ct);offset+=read;if(offset>file.Size)throw new InvalidDataException("Taille reçue supérieure au manifeste.");report($"Téléchargement · {file.File} · reste {(file.Size-offset)/1_000_000d:F0} Mo",100d*(done+offset)/total);}
   }
   report("Vérification SHA-256 · "+file.File,100d*(done+file.Size)/total);
   if(!await Engine.VerifyPath(file,temp,ct)){File.Delete(temp);throw new InvalidDataException("Le fichier ne correspond pas à l’empreinte NVIDIA. Relancez le téléchargement.");}
   File.Move(temp,file.Local,true);done+=file.Size;
  }
  profile.Verified=true;report("Modèles installés et vérifiés",100);
 }
}

