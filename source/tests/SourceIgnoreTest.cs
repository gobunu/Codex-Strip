using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using CodexStrip;
class SourceIgnoreTest {
 static int checks;static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static IEnumerable<T> Children<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static void Age(StripWindow w){typeof(StripWindow).GetField("quietSince",Private).SetValue(w,DateTime.UtcNow.AddSeconds(-65));}
 static async Task ShowSources(StripWindow window){
  var panel=(DependencyObject)typeof(StripWindow).GetField("activePanel",Private).GetValue(window);
  var check=Children<CheckBox>(panel).Single(c=>AutomationProperties.GetAutomationId(c)=="SleepIgnoreSource-durable");
  var scroll=(ScrollViewer)typeof(InsetPanel).GetField("bodyScroll",Private).GetValue(panel);scroll.UpdateLayout();
  var point=check.TranslatePoint(new Point(0,0),(UIElement)scroll.Content);scroll.ScrollToVerticalOffset(Math.Max(0,point.Y-30));
  await Task.Delay(250);window.UpdateLayout();
  var bounds=check.TransformToAncestor(scroll).TransformBounds(new Rect(0,0,check.ActualWidth,check.ActualHeight));
  Check(bounds.Top>=-1&&bounds.Bottom<=scroll.ActualHeight+1,"source option is outside the scroll viewport");
 }
 static void Logic(){
  var sources=new Settings().SleepIgnoreSources;Check(sources.SequenceEqual(new[]{"durable"}),"durable is not ignored by default");
  var cloud=new Card{Id="cloud",Host="durable",Title="Cloud task",TurnId="one"};
  foreach(string status in new[]{"active","completed","input","approval","reconnecting","systemError"}){
   cloud.Status=status;cloud.Unread=true;cloud.CompletionPending=true;cloud.QuestionPending=true;
   Check(SleepPolicy.Quiet(true,new[]{cloud},null,sources),"ignored cloud state blocked idle: "+status);
   var d=SleepDiagnostic.Create(true,new[]{cloud},true,false,false,DateTime.UtcNow,DateTime.UtcNow,3,null,sources);
   Check(d.Blockers.Length==0&&d.Ignored.Single().Card==cloud,"diagnostics disagree with source filter");
   Check(cloud.Unread&&cloud.QuestionPending&&cloud.CompletionPending,"source ignore mutated attention flags");
  }
  var local=new Card{Id="local",Host="local",Title="durable in title",Status="active"};
  Check(!SleepPolicy.Quiet(true,new[]{local},null,sources),"title matched as a source");
  var similar=new Card{Id="other",Host="durable-other",Status="active"};Check(!SleepPolicy.Quiet(true,new[]{similar},null,sources),"source prefix matched another host");
  var ssh=new Card{Id="ssh",Host="remote-ssh-discovered:virgo",Status="active"};var remote=new Card{Id="remote",Host="remote-control:environment",Status="input"};
  Check(SleepPolicy.Quiet(true,new[]{ssh},null,new[]{"remote-ssh"}),"SSH group failed");Check(SleepPolicy.Quiet(true,new[]{remote},null,new[]{"remote-control"}),"Remote Control group failed");
  Check(!SleepPolicy.Quiet(true,new[]{cloud,ssh},null,sources),"ignoring cloud suppressed another source");
  ssh.Title="hb monitor";Check(SleepPolicy.Quiet(true,new[]{cloud,ssh},new[]{"hb"},sources),"title and source rules did not combine");
  Check(SleepDiagnostic.ParseSources(new[]{" DURABLE ","durable","local","invalid",null}).SequenceEqual(new[]{"durable","local"}),"source normalization failed");
  Directory.CreateDirectory(Settings.DataDir);File.WriteAllText(Path.Combine(Settings.DataDir,"settings.json"),J.Json(J.Obj("sleepIgnoreKeywords",new[]{"hb"})));
  var legacy=Settings.Load();Check(legacy.SleepIgnoreSources.SequenceEqual(new[]{"durable"})&&legacy.SleepIgnoreKeywords.SequenceEqual(new[]{"hb"}),"legacy settings did not inherit source default");
  legacy.SleepIgnoreSources.Clear();legacy.Save();Check(Settings.Load().SleepIgnoreSources.Count==0,"explicit empty selection reverted to durable");
  legacy.SleepIgnoreSources.Add("remote-control");legacy.Save();Check(Settings.Load().SleepIgnoreSources.SequenceEqual(new[]{"remote-control"}),"sources did not persist");
  new Settings{SleepIgnoreSources=new List<string>{"durable"},SleepAuto=true,SleepIdleMinutes=1,Topmost=false,TopmostMode="none",ReserveWorkArea=false}.Save();
  var window=new StripWindow();try{
   cloud.Status="active";cloud.QuestionPending=false;window.Engine.Connected=true;window.Engine.Cards[cloud.Key]=cloud;
   Age(window);window.EvaluateSleep();Check(window.Sleeping,"durable blocked automatic sleep");
   cloud.TurnId="two";cloud.Status="input";cloud.QuestionPending=true;window.EvaluateSleep();Check(window.Sleeping,"ignored source woke automatic sleep");
   window.Engine.Cards[local.Key]=local;window.EvaluateSleep();Check(!window.Sleeping,"local activity failed to wake automatic sleep");
   local.Status="idle";window.ShowSleepMonitor();cloud.TurnId="three";window.EvaluateSleep();Check(window.Sleeping,"ignored source woke manual sleep");
   local.Status="active";local.TurnId="new-local-turn";window.EvaluateSleep();Check(!window.Sleeping,"local activity failed to wake manual sleep");
   var editor=new SleepIgnoreEditor(window);var check=Children<CheckBox>(editor).Single(c=>AutomationProperties.GetAutomationId(c)=="SleepIgnoreSource-durable");
   check.IsChecked=false;Check(window.Config.SleepIgnoreSources.Count==0&&Settings.Load().SleepIgnoreSources.Count==0,"unchecked source did not persist");
   check.IsChecked=true;Check(Settings.Load().SleepIgnoreSources.SequenceEqual(new[]{"durable"}),"checked source did not persist");
  }finally{window.Close();}
 }
 static async Task Visuals(StripWindow window,string output){
  await Task.Delay(400);window.Engine.Cards.Clear();var cloud=new Card{Id="cloud-ui",Host="durable",Title=UiText.T("云端任务示例","Cloud task example"),Status="active"};window.Engine.Cards[cloud.Key]=cloud;window.Engine.Connected=true;window.ShowSettings();await Task.Delay(300);
  var panel=(DependencyObject)typeof(StripWindow).GetField("activePanel",Private).GetValue(window);
  var choices=Children<CheckBox>(panel).Where(c=>AutomationProperties.GetAutomationId(c).StartsWith("SleepIgnoreSource-",StringComparison.Ordinal)).ToArray();
  Check(choices.Length==4,"source options missing from settings");var durable=choices.Single(c=>AutomationProperties.GetAutomationId(c)=="SleepIgnoreSource-durable");Check(durable.IsChecked==true,"cloud checkbox is not selected");
  durable.IsChecked=false;Check(window.Config.SleepIgnoreSources.Count==0,"unchecked source did not update the editor");Check(window.CurrentSleepDiagnostic().Blockers.Any(b=>b.Card.Key==cloud.Key),"unchecking source did not restore its blocker");
  durable.IsChecked=true;Check(window.Config.SleepIgnoreSources.SequenceEqual(new[]{"durable"})&&window.CurrentSleepDiagnostic().Ignored.Any(b=>b.Card.Key==cloud.Key),"checking source did not apply");
  Check(window.Config.SleepIgnoreKeywords.SequenceEqual(new[]{"hb"}),"source toggle changed title rules");
  await ShowSources(window);window.Snapshot(Path.Combine(output,"source-ignore-settings-wide.png"));
  window.Width=480;window.Height=900;await Task.Delay(350);await ShowSources(window);window.Snapshot(Path.Combine(output,"source-ignore-settings-portrait.png"));
  foreach(var choice in choices)Check(choice.ActualWidth<=window.ActualWidth-30,"source label exceeds narrow viewport");
  window.Height=315;window.Config.UiScale=1.5;window.ApplyTheme();await Task.Delay(350);await ShowSources(window);window.Snapshot(Path.Combine(output,"source-ignore-settings-minimum.png"));
 }
 [STAThread]static int Main(string[] args){StripWindow window=null;try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Settings.DataDir=Path.Combine(output,"data");var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Logic();
  new Settings{UiScale=1,DefaultUiScale=1,Width=1600,Height=700,ThemeMode="light",Topmost=false,TopmostMode="none",ReserveWorkArea=false,SleepIgnoreKeywords=new List<string>{"hb"}}.Save();
  window=new StripWindow(true);app.MainWindow=window;Exception failure=null;window.Loaded+=async delegate{try{await Visuals(window,output);}catch(Exception e){failure=e;}finally{window.Close();app.Shutdown();}};window.Show();app.Run();if(failure!=null)throw failure;
  Console.WriteLine("PASS: "+checks+" source matching, migration, persistence, idle/wake and UI checks");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(window!=null)window.Close();}}
}
