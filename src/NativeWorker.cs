using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace ReunionLocale;
public record WorkerRequest(string Input,string Output,string Engine,string Asr,string? Diar,string Language,bool Cuda);
public sealed record NativeWord(string word,double start,double end,int speaker);
public static class NativeWorker
{
 const string Dll="nemo_speech_asr_c.dll";
 [StructLayout(LayoutKind.Sequential)] struct Backend {public nuint size;public int gpu;}
 [StructLayout(LayoutKind.Sequential)] struct Model {public nuint size;public IntPtr path,name;}
 [StructLayout(LayoutKind.Sequential)] struct Diar {public nuint size;public IntPtr path;public int chunks,right,left,fifo,cache,period;}
 [StructLayout(LayoutKind.Sequential)] struct Config {public nuint size;public IntPtr backend,model,streaming,decoder,vad,endpointing,postproc,diar,batching,salm;}
 [StructLayout(LayoutKind.Sequential)] struct Options {public nuint size;public IntPtr request,language;public byte interim,wordTimes,punctuation,verbatim,profanity;public int stop;public IntPtr contexts;public nuint contextCount;public int alternatives;public byte diar;public int speakers;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetDllDirectory(string path);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern Options nemo_speech_asr_recognition_options_default();
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int nemo_speech_asr_create(ref Config cfg,out IntPtr recognizer);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern void nemo_speech_asr_destroy(IntPtr recognizer);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int nemo_speech_asr_streaming_recognize(IntPtr recognizer,ref Options options,out IntPtr stream);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int nemo_speech_asr_stream_push_f32(IntPtr stream,float[] samples,nuint count,int rate);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int nemo_speech_asr_stream_next(IntPtr stream,out IntPtr result);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int nemo_speech_asr_stream_finish(IntPtr stream);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern void nemo_speech_asr_stream_close(IntPtr stream);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr nemo_speech_asr_last_error();
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern byte nemo_speech_asr_result_is_final(IntPtr result);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern nuint nemo_speech_asr_result_word_count(IntPtr result,nuint alt);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr nemo_speech_asr_result_word_text(IntPtr result,nuint alt,nuint i);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int nemo_speech_asr_result_word_start_time(IntPtr result,nuint alt,nuint i);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int nemo_speech_asr_result_word_end_time(IntPtr result,nuint alt,nuint i);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int nemo_speech_asr_result_word_speaker_tag(IntPtr result,nuint alt,nuint i);
 [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern void nemo_speech_asr_result_destroy(IntPtr result);
 static void Check(int status){if(status!=0)throw new InvalidOperationException("NVIDIA : "+Marshal.PtrToStringUTF8(nemo_speech_asr_last_error()));}
 sealed class Arena : IDisposable
 {
  readonly List<IntPtr> allocated=[];
  public IntPtr Text(string text){var p=Marshal.StringToCoTaskMemUTF8(text);allocated.Add(p);return p;}
  public IntPtr Struct<T>(T value) where T:struct {var p=Marshal.AllocCoTaskMem(Marshal.SizeOf<T>());Marshal.StructureToPtr(value,p,false);allocated.Add(p);return p;}
  public void Dispose(){foreach(var p in allocated)Marshal.FreeCoTaskMem(p);}
 }
 public static void Run(string requestFile,Action<string> emit)
 {
  var request=Store.Read<WorkerRequest>(requestFile)??throw new InvalidDataException("Requête de transcription absente.");
  var folder=Path.GetDirectoryName(request.Engine)!;
  if(!SetDllDirectory(folder))throw new IOException("Impossible de charger les DLL du moteur.");
  NativeLibrary.SetDllImportResolver(typeof(NativeWorker).Assembly,(name,_,_)=>name==Dll?NativeLibrary.Load(Path.Combine(folder,Dll)):IntPtr.Zero);
  void Report(string stage,double done,double duration)=>emit(JsonSerializer.Serialize(new{stage,processed=done,duration}));
  using var arena=new Arena();
  var config=new Config {size=(nuint)Marshal.SizeOf<Config>(),
   backend=arena.Struct(new Backend{size=(nuint)Marshal.SizeOf<Backend>(),gpu=request.Cuda?0:-1}),
   model=arena.Struct(new Model{size=(nuint)Marshal.SizeOf<Model>(),path=arena.Text(request.Asr)})};
  if(request.Diar!=null)config.diar=arena.Struct(new Diar{size=(nuint)Marshal.SizeOf<Diar>(),path=arena.Text(request.Diar),left=-1});
  using var audio=new AudioFileReader(request.Input);
  double duration=audio.TotalTime.TotalSeconds;
  Report("loading",0,duration);
  IntPtr recognizer=IntPtr.Zero,stream=IntPtr.Zero;
  try {
   Check(nemo_speech_asr_create(ref config,out recognizer));
   var options=nemo_speech_asr_recognition_options_default();options.wordTimes=1;options.interim=1;options.diar=(byte)(request.Diar==null?0:1);options.language=arena.Text(request.Language);
   Check(nemo_speech_asr_streaming_recognize(recognizer,ref options,out stream));
   ISampleProvider source=audio;
   if(source.WaveFormat.Channels==2)source=new StereoToMonoSampleProvider(source){LeftVolume=.5f,RightVolume=.5f};
   else if(source.WaveFormat.Channels!=1)throw new InvalidDataException("Une piste mono ou stéréo est nécessaire.");
   source=new WdlResamplingSampleProvider(source,16000);
   var words=new List<NativeWord>();
   void Drain(){
    while(true){Check(nemo_speech_asr_stream_next(stream,out var result));if(result==IntPtr.Zero)return;
     try{if(nemo_speech_asr_result_is_final(result)!=0)for(nuint i=0;i<nemo_speech_asr_result_word_count(result,0);i++)words.Add(new NativeWord(Marshal.PtrToStringUTF8(nemo_speech_asr_result_word_text(result,0,i))??"",nemo_speech_asr_result_word_start_time(result,0,i)/1000d,nemo_speech_asr_result_word_end_time(result,0,i)/1000d,nemo_speech_asr_result_word_speaker_tag(result,0,i)));}
     finally{nemo_speech_asr_result_destroy(result);}
    }
   }
   float[] chunk=new float[2560];int count;long processed=0;
   while((count=source.Read(chunk,0,chunk.Length))>0){Check(nemo_speech_asr_stream_push_f32(stream,chunk,(nuint)count,16000));Drain();processed+=count;Report("processing",processed/16000d,duration);}
   Report("finalizing",duration,duration);Check(nemo_speech_asr_stream_finish(stream));Drain();
   var unique=words.Distinct().OrderBy(w=>w.start).ToArray();
   File.WriteAllText(request.Output,JsonSerializer.Serialize(new{duration,words=unique}));
   Report("done",duration,duration);
  }
  finally{if(stream!=IntPtr.Zero)nemo_speech_asr_stream_close(stream);if(recognizer!=IntPtr.Zero)nemo_speech_asr_destroy(recognizer);}
 }
}

