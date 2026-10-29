using System.Diagnostics;
namespace ReunionLocale;
public sealed record AnalysisProgress(string Stage,double Percent,TimeSpan Elapsed,TimeSpan? Remaining,bool Estimated=false)
{
 public string Display=>$"{Percent:F0} % · {Stage} · "+(Remaining is {} remaining?$"environ {Format(remaining)} restantes":"Estimation en cours")+(Estimated?" · progression estimée":"");
 static string Format(TimeSpan t)=>t.TotalHours>=1?$"{(int)t.TotalHours} h {t.Minutes:00} min":$"{(int)t.TotalMinutes} min {t.Seconds:00} s";
}
public sealed class ProgressTracker(Action<AnalysisProgress> report)
{
 readonly Stopwatch clock=Stopwatch.StartNew();double last;double? smooth;
 public void Update(string stage,double percent,bool estimated=false)
 {
  last=Math.Clamp(Math.Max(last,percent),0,99);
  TimeSpan? remaining=null;
  if(last>8&&clock.Elapsed.TotalSeconds>2){double raw=Math.Max(0,clock.Elapsed.TotalSeconds*(100-last)/last);smooth=smooth is {} old?.85*old+.15*raw:raw;remaining=TimeSpan.FromSeconds(smooth.Value);}
  report(new AnalysisProgress(stage,last,clock.Elapsed,remaining,estimated));
 }
}

