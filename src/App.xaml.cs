using System.Windows;
using System.IO;
namespace ReunionLocale;
public partial class App : Application
{
 protected override async void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
  if(e.Args.Length==2&&e.Args[0]=="--worker"){
   int code=0;
   using var output=new StreamWriter(Console.OpenStandardOutput()){AutoFlush=true};
   try{NativeWorker.Run(e.Args[1],s=>output.WriteLine(s));}catch(Exception ex){output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{error=ex.Message}));code=1;}
   Shutdown(code);return;
  }
  try{await RuntimeBootstrap.Ensure();var window=new MainWindow();MainWindow=window;ShutdownMode=ShutdownMode.OnMainWindowClose;window.Show();}
  catch(Exception ex){MessageBox.Show("Impossible de préparer le moteur local : "+ex.Message,"VocalFolio");Shutdown(1);}
 }
}

