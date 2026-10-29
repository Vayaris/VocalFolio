using System.IO;
using System.Text;
using System.Text.Json;
namespace ReunionLocale;
public class Settings
{
 public string Storage { get; set; } = Path.Combine(Store.Root,"meetings");
 public string Engine { get; set; } = "";
 public string ModelId { get; set; } = "nemotron";
 public string PlaybackOutput { get; set; } = "";
 public string Input { get; set; } = "";
 public string Output { get; set; } = "";
 public int Language { get; set; }
 public bool Dark { get; set; } = true;
 public bool KeepAudio { get; set; } = true;
 public bool Cuda { get; set; }
}
public class Segment
{
 public double Start { get; set; }
 public double End { get; set; }
 public string VoiceId { get; set; } = "";
 public string Speaker { get; set; } = "";
 public string Text { get; set; } = "";
 public string Track { get; set; } = "pc";
 public string Warning { get; set; } = "";
 public string Stamp => TimeSpan.FromSeconds(Start).ToString(@"hh\:mm\:ss");
}
public class Meeting
{
 public string Id { get; set; } = Guid.NewGuid().ToString("N");
 public string Title { get; set; } = "Réunion";
 public DateTime Created { get; set; } = DateTime.Now;
 public string State { get; set; } = "Enregistrement interrompu — audio récupérable";
 public List<Segment> Segments { get; set; } = [];
 public double Duration { get; set; }
 public string ModelId { get; set; } = "nemotron";
 public string ModelRevision { get; set; } = "";
 public string Directory { get; set; } = "";
 public string Label => $"{Created:dd/MM/yyyy HH:mm}   ·   {Title}   ·   {State}";
}
public static class Store
{
 public static string Root => Environment.GetEnvironmentVariable("REUNION_LOCALE_DATA") is {Length:>0} testRoot ? Path.GetFullPath(testRoot) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ReunionLocale");
 public static string Models => Path.Combine(Root,"models");
 static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
 public static T? Read<T>(string file) => File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file)) : default;
 public static void Write<T>(string file,T data)
 {
  System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
  File.WriteAllText(file+".tmp",JsonSerializer.Serialize(data,Options),Encoding.UTF8);
  File.Move(file+".tmp",file,true);
 }
 public static void Save(Meeting m) => Write(Path.Combine(m.Directory,"meeting.json"),m);
 public static List<Meeting> History(string root)
 {
  if (!System.IO.Directory.Exists(root)) return [];
  var meetings = new List<Meeting>();
  foreach(var f in System.IO.Directory.EnumerateFiles(root,"meeting.json",SearchOption.AllDirectories))
   try { if(Read<Meeting>(f) is {} m) { m.Directory=Path.GetDirectoryName(f)!; meetings.Add(m); } } catch(JsonException) { }
  return meetings.OrderByDescending(m=>m.Created).ToList();
 }
 public static string Export(Meeting m,bool srt)
 {
  static string Time(double s) => TimeSpan.FromSeconds(Math.Max(0,s)).ToString(@"hh\:mm\:ss\,fff");
  var b=new StringBuilder(); int n=0;
  foreach(var s in m.Segments.OrderBy(s=>s.Start))
   if(srt) b.AppendLine($"{++n}\n{Time(s.Start)} --> {Time(Math.Max(s.Start+.1,s.End))}\n{s.Speaker} : {s.Text}\n");
   else b.AppendLine($"[{s.Stamp}] {s.Speaker} : {s.Text}");
  return b.ToString();
 }
}

