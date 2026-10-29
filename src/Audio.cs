using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
namespace ReunionLocale;
public record AudioDevice(string Id,string Name);
public sealed class LevelMonitor : IDisposable
{
 readonly List<WasapiCapture> captures=[];
 public LevelMonitor(string input,string output,Action<int,float> level)
 {
  try { using var en=new MMDeviceEnumerator();
   captures.Add(new WasapiCapture(en.GetDevice(input)));captures.Add(new WasapiLoopbackCapture(en.GetDevice(output)));
   for(int i=0;i<captures.Count;i++){int index=i;var c=captures[i];c.WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(c.WaveFormat.SampleRate,c.WaveFormat.Channels);c.DataAvailable+=(_,e)=>{float peak=0;for(int n=0;n+4<=e.BytesRecorded;n+=4)peak=Math.Max(peak,Math.Abs(BitConverter.ToSingle(e.Buffer,n)));level(index,peak);};c.StartRecording();}
  }catch{Dispose();throw;}
 }
 public void Dispose(){foreach(var c in captures){c.StopRecording();c.Dispose();}captures.Clear();}
}
// One monotonic clock and one writer per source. Missing packets become silence.
public sealed class AudioSession : IDisposable
{
 readonly Stopwatch clock=Stopwatch.StartNew();
 readonly List<AudioTrack> tracks=[];
 readonly object gate=new();
 readonly Timer timer;
 bool paused, disposed;
 public double Seconds => clock.Elapsed.TotalSeconds;
 public event Action<int,float>? Level;
 public event Action<string>? Fault;
 public AudioSession(string input,string output,string folder)
 {
  try {
   using var en=new MMDeviceEnumerator();
   tracks.Add(new AudioTrack(new WasapiCapture(en.GetDevice(input)),Path.Combine(folder,"mic.wav"),0,()=>Seconds,(i,v)=>Level?.Invoke(i,v),s=>Fault?.Invoke(s),gate));
   tracks.Add(new AudioTrack(new WasapiLoopbackCapture(en.GetDevice(output)),Path.Combine(folder,"pc.wav"),1,()=>Seconds,(i,v)=>Level?.Invoke(i,v),s=>Fault?.Invoke(s),gate));
   foreach(var t in tracks) t.Start();
   timer=new Timer(_=> { lock(gate) if(!paused&&!disposed) foreach(var t in tracks) t.Pad(Seconds-.15); },null,250,250);
  } catch { foreach(var t in tracks)t.Dispose(); throw; }
 }
 public void Pause(bool value) { lock(gate) { if(value==paused)return; if(value) {clock.Stop();foreach(var t in tracks)t.Pad(Seconds);} else clock.Start(); paused=value; foreach(var t in tracks)t.Paused=value; } }
 public void Dispose()
 {
  lock(gate) { if(disposed)return; disposed=true;clock.Stop();timer.Dispose();foreach(var t in tracks)t.Paused=true; }
  foreach(var t in tracks)t.Stop();
  lock(gate)foreach(var t in tracks){t.Pad(Seconds);t.Dispose();}
 }
 sealed class AudioTrack : IDisposable
 {
  readonly WasapiCapture capture; readonly WaveFileWriter writer; readonly int rate,channels,index;
  readonly Func<double> now; readonly Action<int,float> level;readonly Action<string> fault; readonly object gate;
  readonly ManualResetEventSlim stopped=new(false);long frames; bool ended;
  public bool Paused;
  public AudioTrack(WasapiCapture capture,string file,int index,Func<double> now,Action<int,float> level,Action<string> fault,object gate)
  {
   this.capture=capture; this.index=index;this.now=now;this.level=level;this.fault=fault;this.gate=gate;
   capture.WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(capture.WaveFormat.SampleRate,capture.WaveFormat.Channels);
   rate=capture.WaveFormat.SampleRate;channels=capture.WaveFormat.Channels;
   writer=new WaveFileWriter(file,new WaveFormat(rate,16,1));
   capture.DataAvailable+=Data;
   capture.RecordingStopped+=(_,e)=>{stopped.Set();if(e.Exception!=null)fault($"Source {(index==0?"micro":"sortie")} interrompue : {e.Exception.Message}");};
  }
  public void Start()=>capture.StartRecording();
  public void Stop(){capture.StopRecording();stopped.Wait(TimeSpan.FromSeconds(3));}
  public void Pad(double seconds)
  {
   long missing=(long)(Math.Max(0,seconds)*rate)-frames;
   byte[] zeros=new byte[8192];
   while(missing>0){int count=(int)Math.Min(missing,zeros.Length/2);writer.Write(zeros,0,count*2);frames+=count;missing-=count;}
   writer.Flush();
  }
  void Data(object? sender,WaveInEventArgs e)
  {
   lock(gate){if(Paused||ended)return;
    int count=e.BytesRecorded/(4*channels); Pad(now()-(double)count/rate);
    byte[] pcm=new byte[count*2];float peak=0;
    for(int i=0;i<count;i++){float sum=0;for(int c=0;c<channels;c++)sum+=BitConverter.ToSingle(e.Buffer,(i*channels+c)*4);float sample=Math.Clamp(sum/channels,-1,1);peak=Math.Max(peak,Math.Abs(sample));short s=(short)(sample*32767);pcm[i*2]=(byte)s;pcm[i*2+1]=(byte)(s>>8);}
    writer.Write(pcm,0,pcm.Length);frames+=count;level(index,peak);
   }
  }
  public void Dispose(){ended=true;capture.Dispose();writer.Dispose();stopped.Dispose();}
 }
}

