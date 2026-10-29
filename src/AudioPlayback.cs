using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace ReunionLocale;
public sealed class AudioPlayback : IDisposable
{
 WasapiOut? output;AudioFileReader? reader;MMDevice? device;
 public event Action<Exception?>? Stopped;
 public static OffsetSampleProvider CreateBounded(ISampleProvider source,double start,double end,double duration)
 {
  start=Math.Clamp(start,0,duration);end=Math.Clamp(end,start,duration);
  if(end-start<.02)throw new InvalidOperationException("Ce passage ne contient pas de durée audio lisible.");
  return new OffsetSampleProvider(source){SkipOver=TimeSpan.FromSeconds(start),Take=TimeSpan.FromSeconds(end-start)};
 }
 public void Play(string path,double start,double end,string? outputId)
 {
  using var en=new MMDeviceEnumerator();
  device=string.IsNullOrEmpty(outputId)?en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):en.GetDevice(outputId);
  reader=new AudioFileReader(path);
  var bounded=CreateBounded(reader,start,end,reader.TotalTime.TotalSeconds);
  output=new WasapiOut(device,AudioClientShareMode.Shared,true,100);
  output.PlaybackStopped+=(_,e)=>Stopped?.Invoke(e.Exception);
  output.Init(bounded);output.Play();
 }
 public void Dispose(){if(output!=null){output.Stop();output.Dispose();output=null;}reader?.Dispose();reader=null;device?.Dispose();device=null;}
}

