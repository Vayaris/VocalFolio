using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace ReunionLocale;
public partial class MainWindow : Window
{
 Settings settings;
 Meeting? current;
 AudioSession? session;
 LevelMonitor? monitor;
 CancellationTokenSource? work;
 bool paused;
 AudioPlayback? player;
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
 readonly HashSet<string> verifiedModels=[];
 readonly string settingsFile=Path.Combine(Store.Root,"settings.json");
 public MainWindow()
 {
  InitializeComponent();
  try{settings=Store.Read<Settings>(settingsFile)??new();}catch{settings=new();}
  var legacyEngine=settings.Engine;
  if(!settings.Cuda||!File.Exists(settings.Engine)){settings.Engine=RuntimeBootstrap.EnginePath;settings.Cuda=false;}
  Storage.Text=settings.Storage;EnginePath.Text=settings.Engine;LanguageChoice.SelectedIndex=settings.Language;Dark.IsChecked=settings.Dark;KeepAudio.IsChecked=settings.KeepAudio;Cuda.IsChecked=settings.Cuda;
  ApplyTheme();LoadDevices();LoadHistory();
  ModelChoice.ItemsSource=ModelCatalog.All;ModelChoice.SelectedItem=ModelCatalog.Get(settings.ModelId);UpdateModelDetails();
  timer.Tick+=(_,_)=>{if(session!=null)TimerLabel.Text=TimeSpan.FromSeconds(session.Seconds).ToString(@"hh\:mm\:ss");};timer.Start();
  Loaded+=async(_,_)=>await MigrateModels(legacyEngine);
 }
 void Report(string text,double progress=0){Dispatcher.Invoke(()=>{Status.Text=text;WorkProgress.Value=progress;ProgressDetails.Text=$"{progress:F0} %";});}
 void ReportAnalysis(AnalysisProgress progress){Dispatcher.Invoke(()=>{Status.Text=progress.Stage;WorkProgress.Value=progress.Percent;ProgressDetails.Text=progress.Display+$" · écoulé {progress.Elapsed:mm\\:ss}";});}
 void Error(Exception ex){Report(ex.Message);MessageBox.Show(this,ex.Message,"VocalFolio",MessageBoxButton.OK,MessageBoxImage.Information);}
 void LoadDevices()
 {
  try{
   using var en=new MMDeviceEnumerator();
   List<AudioDevice> Get(DataFlow flow){var devices=en.EnumerateAudioEndPoints(flow,DeviceState.Active);return devices.Select(d=>new AudioDevice(d.ID,d.FriendlyName)).ToList();}
   var inputs=Get(DataFlow.Capture);var outputs=Get(DataFlow.Render);
   Inputs.ItemsSource=inputs;Outputs.ItemsSource=outputs;
   string Default(DataFlow flow){try{using var d=en.GetDefaultAudioEndpoint(flow,Role.Multimedia);return d.ID;}catch{return "";}}
   Inputs.SelectedItem=inputs.FirstOrDefault(d=>d.Id==settings.Input)??inputs.FirstOrDefault(d=>d.Id==Default(DataFlow.Capture))??inputs.FirstOrDefault();Outputs.SelectedItem=outputs.FirstOrDefault(d=>d.Id==settings.Output)??outputs.FirstOrDefault(d=>d.Id==Default(DataFlow.Render))??outputs.FirstOrDefault();
   ListeningOutputs.ItemsSource=outputs;ListeningOutputs.SelectedItem=outputs.FirstOrDefault(d=>d.Id==settings.PlaybackOutput)??outputs.FirstOrDefault(d=>d.Id==Default(DataFlow.Render))??outputs.FirstOrDefault();
   if(inputs.Count==0||outputs.Count==0)Report("Une entrée et une sortie actives sont nécessaires. Vérifiez les périphériques Windows.");
  }catch(Exception ex){Error(ex);}
 }
 void RefreshDevices(object s,RoutedEventArgs e){if(session==null){StopMonitor();LoadDevices();}}
 void StopMonitor(){monitor?.Dispose();monitor=null;TestButton.Content="Tester les niveaux";MicLevel.Value=PcLevel.Value=0;}
 void TestLevels(object s,RoutedEventArgs e)
 {
  if(session!=null||work!=null)return;
  if(monitor!=null){StopMonitor();return;}
  try{if(Inputs.SelectedItem is not AudioDevice input||Outputs.SelectedItem is not AudioDevice output)return;
   monitor=new LevelMonitor(input.Id,output.Id,(i,v)=>Dispatcher.BeginInvoke(()=>{if(i==0)MicLevel.Value=v;else PcLevel.Value=v;}));TestButton.Content="Arrêter le test";Report("Test des sources · aucun audio enregistré");
  }catch(Exception ex){StopMonitor();Error(ex);}
 }
 void CaptureSettings()
 {
  settings.Storage=Path.GetFullPath(Storage.Text.Trim());settings.Engine=EnginePath.Text.Trim();settings.Language=LanguageChoice.SelectedIndex;settings.Dark=Dark.IsChecked==true;settings.KeepAudio=KeepAudio.IsChecked==true;settings.Cuda=Cuda.IsChecked==true;
  settings.Input=(Inputs.SelectedItem as AudioDevice)?.Id??"";settings.Output=(Outputs.SelectedItem as AudioDevice)?.Id??"";
  settings.PlaybackOutput=(ListeningOutputs.SelectedItem as AudioDevice)?.Id??"";
  Store.Write(settingsFile,settings);
 }
 void SaveSettings(object s,RoutedEventArgs e){try{CaptureSettings();LoadHistory();Report("Paramètres enregistrés.");}catch(Exception ex){Error(ex);}}
 void BrowseStorage(object s,RoutedEventArgs e){var d=new OpenFolderDialog{Title="Dossier local des réunions"};if(d.ShowDialog()==true)Storage.Text=d.FolderName;}
 void BrowseEngine(object s,RoutedEventArgs e){var d=new OpenFileDialog{Title="Moteur NVIDIA compatible",Filter="Moteur NVIDIA|nemo-speech.exe"};if(d.ShowDialog()==true)EnginePath.Text=d.FileName;}
 void ApplyTheme()
 {
  bool dark=Dark.IsChecked==true;
  Resources["Bg"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#101725":"#F3F6FA"));
  Resources["Panel"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#1B2639":"#FFFFFF"));
  Resources["Ink"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#F0F4FA":"#152239"));
 }
 void ThemeChanged(object s,RoutedEventArgs e){if(IsInitialized)ApplyTheme();}
 void SetBusy(bool busy)
 {
  CancelButton.IsEnabled=busy;ModelChoice.IsEnabled=!busy;SaveSettingsButton.IsEnabled=!busy&&session==null;TestButton.IsEnabled=!busy&&session==null;StartButton.IsEnabled=!busy&&session==null&&RecordingPermission.IsChecked==true;
  Inputs.IsEnabled=Outputs.IsEnabled=!busy&&session==null;
  UpdateModelDetails();
 }
 void StartRecording(object s,RoutedEventArgs e)
 {
  if(work!=null||session!=null)return;
  if(RecordingPermission.IsChecked!=true){Report("Confirmez d’abord disposer des autorisations nécessaires à cet enregistrement.");return;}
  try{
   StopAudio();StopMonitor();CaptureSettings();
   if(Inputs.SelectedItem is not AudioDevice input||Outputs.SelectedItem is not AudioDevice output)throw new InvalidOperationException("Sélectionnez une entrée micro et une sortie audio.");
   current=new Meeting{Title=string.IsNullOrWhiteSpace(MeetingTitle.Text)?"Réunion":MeetingTitle.Text.Trim()};
   current.Directory=Path.Combine(settings.Storage,current.Created.ToString("yyyy-MM-dd_HHmmss")+"_"+current.Id[..6]);
   Store.Save(current);
   session=new AudioSession(input.Id,output.Id,current.Directory);
   RecordingPermission.IsChecked=false;
   session.Level+=(i,v)=>Dispatcher.BeginInvoke(()=>{if(i==0)MicLevel.Value=v;else PcLevel.Value=v;});
   session.Fault+=message=>Dispatcher.BeginInvoke(()=>Report(message+" · la piste disponible continue d’être enregistrée."));
   paused=false;PauseButton.Content="Pause";StartButton.IsEnabled=TestButton.IsEnabled=false;PauseButton.IsEnabled=StopButton.IsEnabled=true;Inputs.IsEnabled=Outputs.IsEnabled=false;ModelChoice.IsEnabled=false;UpdateModelDetails();Transcript.ItemsSource=null;UpdatePlaybackControls();
   Report("Enregistrement en cours · audio conservé localement");
  }catch(Exception ex){session?.Dispose();session=null;Error(ex);}
 }
 void PermissionChanged(object s,RoutedEventArgs e){if(StartButton!=null)StartButton.IsEnabled=RecordingPermission.IsChecked==true&&session==null&&work==null;}
 void ShowLegal(object s,RoutedEventArgs e)=>MessageBox.Show(this,RecordingNotice.Text,"VocalFolio — confidentialité et usage responsable",MessageBoxButton.OK,MessageBoxImage.Information);
 void PauseRecording(object s,RoutedEventArgs e){if(session==null)return;paused=!paused;session.Pause(paused);PauseButton.Content=paused?"Reprendre":"Pause";Report(paused?"Enregistrement en pause":"Enregistrement en cours");}
 async void StopRecording(object s,RoutedEventArgs e)
 {
  if(session==null||current==null)return;
  var recording=session;current.Duration=recording.Seconds;session=null;
  try{recording.Dispose();current.State="Audio enregistré";Store.Save(current);}catch(Exception ex){Error(ex);}
  PauseButton.IsEnabled=StopButton.IsEnabled=false;MicLevel.Value=PcLevel.Value=0;SetBusy(false);LoadHistory();await Analyze();
 }
 async Task Analyze()
 {
  if(current==null||work!=null||session!=null)return;
  work=new CancellationTokenSource();SetBusy(true);StopAudio();StopMonitor();
  var meeting=current;
  var selectedSettings=new Settings{Engine=EnginePath.Text.Trim(),ModelId=settings.ModelId,Language=LanguageChoice.SelectedIndex,Cuda=Cuda.IsChecked==true,KeepAudio=KeepAudio.IsChecked==true};
  try{
   CaptureSettings();var profile=ModelCatalog.Get(selectedSettings.ModelId);
   if(!await ModelManager.IsInstalled(profile,work.Token)){Report("Installation automatique des modèles nécessaires…");await ModelManager.Install(profile,Report,work.Token);}
   var segments=await Engine.TranscribeDetailed(meeting,selectedSettings,ReportAnalysis,work.Token);
   var previousNames=meeting.Segments.GroupBy(s=>s.VoiceId).ToDictionary(g=>g.Key,g=>g.First().Speaker);
   foreach(var segment in segments)if(previousNames.TryGetValue(segment.VoiceId,out var name))segment.Speaker=name;
   meeting.Segments=segments;meeting.ModelId=profile.Id;meeting.ModelRevision=profile.Asr!.Revision;meeting.State="Transcrite";Store.Save(meeting);
   if(!selectedSettings.KeepAudio){foreach(var track in new[]{"mic","pc"})File.Delete(Path.Combine(meeting.Directory,track+".wav"));}
   Transcript.ItemsSource=meeting.Segments;Tabs.SelectedIndex=1;UpdatePlaybackControls();Report($"Transcription terminée · {segments.Count} passages",100);
  }catch(OperationCanceledException){meeting.State="Analyse annulée — audio conservé";Store.Save(meeting);Report(meeting.State);}
  catch(Exception ex){meeting.State="Analyse à relancer";Store.Save(meeting);Error(ex);}
  finally{work.Dispose();work=null;SetBusy(false);LoadHistory();}
 }
 async void DownloadModels(object s,RoutedEventArgs e)
 {
  await InstallSelected(false);
 }
 async Task InstallSelected(bool activate,string[]? sources=null)
 {
  if(work!=null||session!=null||ModelChoice.SelectedItem is not ModelProfile profile||!profile.Supported)return;
  StopMonitor();work=new();SetBusy(true);
  try{
   if(sources==null)await ModelManager.Install(profile,Report,work.Token);else await ModelManager.Import(profile,sources,Report,work.Token);
   verifiedModels.Add(profile.Id);
   if(activate){var previous=settings.ModelId;try{settings.ModelId=profile.Id;CaptureSettings();}catch{settings.ModelId=previous;throw;}}
   Report(activate?"Modèle actif : "+profile.Name:"Modèles installés et vérifiés · "+profile.Name,100);
  }catch(OperationCanceledException){Report("Installation annulée · relancez pour reprendre le téléchargement.");}catch(Exception ex){Error(ex);}finally{work.Dispose();work=null;SetBusy(false);ModelChoice.Items.Refresh();}
 }
 async void UseModel(object s,RoutedEventArgs e)=>await InstallSelected(true);
 async void ImportModels(object s,RoutedEventArgs e){var d=new OpenFileDialog{Title="GGUF officiels du profil choisi (Q8)",Filter="Modèles GGUF|*.gguf",Multiselect=true};if(d.ShowDialog()==true)await InstallSelected(false,d.FileNames);}
 void OpenModelsFolder(object s,RoutedEventArgs e){try{Directory.CreateDirectory(Store.Models);OpenFolder(Store.Models);}catch(Exception ex){Error(ex);}}
 void ModelChanged(object s,SelectionChangedEventArgs e){if(ModelDetails!=null)UpdateModelDetails();}
 void UpdateModelDetails()
 {
  if(ModelChoice.SelectedItem is not ModelProfile profile)return;
  ModelDetails.Text=profile.Details+(verifiedModels.Contains(profile.Id)?"\nInstallé — intégrité vérifiée":"");ActiveModelLabel.Text="Modèle actif : "+ModelCatalog.Get(settings.ModelId).Name;
  DownloadButton.IsEnabled=ImportButton.IsEnabled=UseModelButton.IsEnabled=profile.Supported&&work==null&&session==null;
 }
 async Task MigrateModels(string legacyEngine)
 {
  if(work!=null)return;work=new();SetBusy(true);
  try{
   var paths=new List<string>{Path.Combine(AppContext.BaseDirectory,"models"),Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","app","models"))};
   if(!string.IsNullOrWhiteSpace(legacyEngine)&&Path.IsPathFullyQualified(legacyEngine))paths.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(legacyEngine)!,"..","..","models")));
   await ModelManager.Migrate(paths,Report,work.Token);
   var profile=ModelCatalog.Get(settings.ModelId);if(await ModelManager.IsInstalled(profile,work.Token)){verifiedModels.Add(profile.Id);Report("Prêt · modèles locaux vérifiés");}else Report("Prêt · les modèles seront installés automatiquement à la première transcription");
  }catch(OperationCanceledException){Report("Réutilisation des modèles annulée. Vous pourrez les importer depuis Paramètres.");}catch(Exception ex){Error(ex);}finally{work.Dispose();work=null;SetBusy(false);ModelChoice.Items.Refresh();}
 }
 void CancelWork(object s,RoutedEventArgs e)=>work?.Cancel();
 void LoadHistory(){try{History.ItemsSource=Store.History(settings.Storage).Where(m=>m.Title.Contains(Search.Text??"",StringComparison.CurrentCultureIgnoreCase)).ToList();}catch(Exception ex){Report("Historique indisponible : "+ex.Message);}}
 void SearchHistory(object s,TextChangedEventArgs e){if(settings!=null&&History!=null)LoadHistory();}
 bool CanNavigate()=>session==null&&work==null;
 void OpenMeeting(object s,RoutedEventArgs e){if(!CanNavigate()||History.SelectedItem is not Meeting m)return;StopAudio();current=m;Transcript.ItemsSource=m.Segments;Tabs.SelectedIndex=1;UpdatePlaybackControls();Report(m.Title+" · "+m.State+" · "+ModelCatalog.Get(m.ModelId).Name);}
 async void RetryMeeting(object s,RoutedEventArgs e){if(!CanNavigate()||History.SelectedItem is not Meeting m)return;current=m;await Analyze();}
 void DeleteMeeting(object s,RoutedEventArgs e)
 {
  if(!CanNavigate()||History.SelectedItem is not Meeting m)return;
  if(MessageBox.Show(this,$"Supprimer définitivement « {m.Title} » et ses fichiers ?","Supprimer une réunion",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
  try{StopAudio();string root=Path.GetFullPath(settings.Storage)+Path.DirectorySeparatorChar;string target=Path.GetFullPath(m.Directory);if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(target,"meeting.json")))throw new IOException("Dossier de réunion invalide.");Directory.Delete(target,true);if(current?.Id==m.Id){current=null;Transcript.ItemsSource=null;}LoadHistory();}catch(Exception ex){Error(ex);}
 }
 void SaveTranscript(){if(current==null)return;Transcript.CommitEdit(DataGridEditingUnit.Cell,true);Transcript.CommitEdit(DataGridEditingUnit.Row,true);Store.Save(current);}
 void SaveEdits(object s,RoutedEventArgs e){try{SaveTranscript();Report("Corrections enregistrées.");}catch(Exception ex){Error(ex);}}
 void RenameSpeaker(object s,RoutedEventArgs e)
 {
  if(Transcript.SelectedItem is not Segment seg||current==null)return;
  var box=new TextBox{Text=seg.Speaker,Margin=new Thickness(16),MinWidth=300};var ok=new Button{Content="Appliquer à cette voix",Margin=new Thickness(16)};var stack=new StackPanel();stack.Children.Add(box);stack.Children.Add(ok);
  var dialog=new Window{Title="Nom de l’intervenant",Content=stack,SizeToContent=SizeToContent.WidthAndHeight,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};ok.Click+=(_,_)=>{if(!string.IsNullOrWhiteSpace(box.Text))dialog.DialogResult=true;};
  if(dialog.ShowDialog()==true){foreach(var row in current.Segments.Where(x=>x.VoiceId==seg.VoiceId))row.Speaker=box.Text.Trim();Transcript.Items.Refresh();SaveTranscript();}
 }
 void PlaySegment(object s,RoutedEventArgs e)
 {
  if(Transcript.SelectedItem is not Segment seg||current==null)return;
  try{
   StopAudio();string file=TrackPath(seg);if(!File.Exists(file))throw new IOException("Audio non conservé pour cette réunion.");
   var playback=new AudioPlayback();player=playback;
   playback.Stopped+=error=>Dispatcher.BeginInvoke(()=>{if(!ReferenceEquals(player,playback))return;StopAudio();PlaybackStatus.Text=error==null?"Lecture terminée.":"Erreur de lecture : "+error.Message;});
   playback.Play(file,seg.Start,seg.End,(ListeningOutputs.SelectedItem as AudioDevice)?.Id);
   PlaybackStatus.Text=$"Lecture · {seg.Speaker} · {seg.Stamp} · {(ListeningOutputs.SelectedItem as AudioDevice)?.Name}";
  }catch(Exception ex){StopAudio();PlaybackStatus.Text="Erreur de lecture : "+ex.Message;Error(ex);}
 }
 string TrackPath(Segment seg)=>Path.Combine(current!.Directory,seg.Track=="mic"?"mic.wav":"pc.wav");
 void StopAudio(){var old=player;player=null;old?.Dispose();}
 void StopPlayback(object s,RoutedEventArgs e){StopAudio();PlaybackStatus.Text="Lecture arrêtée.";}
 void ListeningChanged(object s,SelectionChangedEventArgs e){StopAudio();}
 void TranscriptSelectionChanged(object s,SelectionChangedEventArgs e){StopAudio();UpdatePlaybackControls();}
 void UpdatePlaybackControls()
 {
  if(PlayButton==null)return;OpenAudioButton.IsEnabled=current!=null;
  bool selected=Transcript.SelectedItem is Segment;
  PlayButton.IsEnabled=current!=null&&Transcript.SelectedItem is Segment seg&&File.Exists(TrackPath(seg));
  PlaybackStatus.Text=!selected?"Sélectionnez un passage pour le réécouter.":!PlayButton.IsEnabled?"Le fichier audio de ce passage n’est plus disponible (non conservé ou déplacé).":"Prêt à réécouter le passage sélectionné.";
 }
 static void OpenFolder(string folder){var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};info.ArgumentList.Add(Path.GetFullPath(folder));Process.Start(info);}
 void OpenAudioFolder(object s,RoutedEventArgs e)
 {
  if(current==null)return;
  try{if(!Directory.Exists(current.Directory))throw new DirectoryNotFoundException("Le dossier de cette réunion n’existe plus.");OpenFolder(current.Directory);if(!File.Exists(Path.Combine(current.Directory,"mic.wav"))&&!File.Exists(Path.Combine(current.Directory,"pc.wav")))Report("Dossier ouvert · les pistes audio n’ont pas été conservées.");}catch(Exception ex){Error(ex);}
 }
 void Export(bool srt)
 {
  if(current==null)return;
  var dialog=new SaveFileDialog{Filter=srt?"Sous-titres SRT|*.srt":"Texte|*.txt",FileName="transcription"+(srt?".srt":".txt")};
  try{if(dialog.ShowDialog()==true){SaveTranscript();File.WriteAllText(dialog.FileName,Store.Export(current,srt),System.Text.Encoding.UTF8);Report("Export enregistré.");}}catch(Exception ex){Error(ex);}
 }
 void ExportTxt(object s,RoutedEventArgs e)=>Export(false);
 void ExportSrt(object s,RoutedEventArgs e)=>Export(true);
 void OnClosing(object? s,CancelEventArgs e)
 {
  if(work!=null){e.Cancel=true;Report("Annulez l’opération et attendez sa fin avant de fermer.");return;}
  if(session!=null){if(MessageBox.Show(this,"Arrêter l’enregistrement et conserver l’audio avant de fermer ?","Enregistrement actif",MessageBoxButton.YesNo)!=MessageBoxResult.Yes){e.Cancel=true;return;}if(current!=null){current.Duration=session.Seconds;current.State="Audio enregistré — analyse à relancer";}session.Dispose();session=null;if(current!=null)Store.Save(current);}
  StopAudio();StopMonitor();try{SaveTranscript();CaptureSettings();}catch{/* Never discard recoverable audio due to a settings error. */}
 }
}


