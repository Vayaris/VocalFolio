using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http;
namespace ReunionLocale;
public static class RuntimeBootstrap
{
 public const string Version="vocalfolio-1.0.0-cpu-66d50d9";
 public static string Home=>Path.Combine(Store.Root,"runtime",Version);
 public static string EnginePath=>Path.Combine(Home,"bin","nemo-speech.exe");
 public static async Task<string> Ensure(CancellationToken ct=default)
 {
  var assembly=Assembly.GetExecutingAssembly();
  using var manifest=assembly.GetManifestResourceStream("runtime-manifest.json")!;
  var hashes=(await JsonSerializer.DeserializeAsync<Dictionary<string,string>>(manifest,cancellationToken:ct))!;
  Directory.CreateDirectory(Path.GetDirectoryName(Home)!);
  // A short-lived lock serializes first extraction across GUI / worker instances.
  FileStream? lease=null;
  for(int i=0;lease==null;i++){ct.ThrowIfCancellationRequested();try{lease=new FileStream(Home+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException) when(i<120){await Task.Delay(250,ct);}}
  await using var held=lease;
  async Task<bool> Valid(string path,string sha){if(!File.Exists(path))return false;await using var f=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(f,ct)).Equals(sha,StringComparison.OrdinalIgnoreCase);}
  using var zipStream=assembly.GetManifestResourceStream("runtime.zip")!;
  using var zip=new ZipArchive(zipStream,ZipArchiveMode.Read);
  foreach(var pair in hashes){
   var target=Path.GetFullPath(Path.Combine(Home,pair.Key));
   if(!target.StartsWith(Home+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Chemin de moteur invalide.");
   if(await Valid(target,pair.Value))continue;
   var entry=zip.GetEntry(pair.Key.Replace('\\','/'))??throw new InvalidDataException("Composant embarqué manquant : "+pair.Key);
   Directory.CreateDirectory(Path.GetDirectoryName(target)!);
   await using(var source=entry.Open())await using(var dest=File.Create(target+".part"))await source.CopyToAsync(dest,ct);
   if(!await Valid(target+".part",pair.Value))throw new InvalidDataException("Échec de vérification du moteur embarqué.");
   File.Move(target+".part",target,true);
  }
  await EnsureVendorDependencies(ct);
  return EnginePath;
 }
 static async Task EnsureVendorDependencies(CancellationToken ct)
 {
  using var manifest=Assembly.GetExecutingAssembly().GetManifestResourceStream("vendor-runtime.json")!;
  var hashes=(await JsonSerializer.DeserializeAsync<Dictionary<string,string>>(manifest,cancellationToken:ct))!;
  var missing=new Dictionary<string,string>();
  foreach(var pair in hashes){
   string local=Path.Combine(Home,"bin",pair.Key);
   if(File.Exists(local)){await using var file=File.OpenRead(local);if(Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(pair.Value,StringComparison.OrdinalIgnoreCase))continue;}
   if(File.Exists(Path.Combine(Environment.SystemDirectory,pair.Key)))continue;
   missing.Add(pair.Key,pair.Value);
  }
  if(missing.Count==0)return;
  // Proprietary VC runtime binaries are obtained directly by the user from
  // NVIDIA's official release. They are NOT redistributed inside VocalFolio.
  string archive=Path.Combine(Home,"vendor-download.zip.part");
  using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(2)};
  client.DefaultRequestHeaders.UserAgent.ParseAdd("VocalFolio/1.0.0");
  client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
  using var response=await client.GetAsync("https://api.github.com/repos/NVIDIA/NeMo-Speech.cpp/releases/assets/686263624",HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
  await using(var input=await response.Content.ReadAsStreamAsync(ct))await using(var output=File.Create(archive))await input.CopyToAsync(output,ct);
  await using(var file=File.OpenRead(archive))if(!Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals("a43f373bd78b2b4ab90167bc838c6c4c7f1a0ae526bfb3bc237e46b01165c8de",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("La dépendance officielle ne correspond plus à l’empreinte attendue. Consultez la page GitHub de VocalFolio pour une version actualisée.");
  using(var zip=ZipFile.OpenRead(archive))foreach(var pair in missing){
   var entry=zip.Entries.SingleOrDefault(e=>e.FullName.EndsWith("/bin/"+pair.Key,StringComparison.OrdinalIgnoreCase))??throw new InvalidDataException("Dépendance absente de l’archive officielle : "+pair.Key);
   string target=Path.Combine(Home,"bin",pair.Key);
   await using(var input=entry.Open())await using(var output=File.Create(target+".part"))await input.CopyToAsync(output,ct);
   await using(var file=File.OpenRead(target+".part"))if(!Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(pair.Value,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Dépendance officielle corrompue : "+pair.Key);
   File.Move(target+".part",target,true);
  }
  File.Delete(archive);
 }
}

