using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexStrip;

class AutoSleepTest {
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static T Field<T>(StripWindow window,string name){return (T)typeof(StripWindow).GetField(name,Private).GetValue(window);}
 static void Age(StripWindow window,int seconds){typeof(StripWindow).GetField("quietSince",Private).SetValue(window,DateTime.UtcNow.AddSeconds(-seconds));}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void ExpectSleep(StripWindow window){Age(window,61);window.EvaluateSleep();Check(window.Sleeping,"idle panel prevented automatic sleep");Check(Field<Grid>(window,"sleepLayer").Visibility==Visibility.Visible&&Field<Border>(window,"taskPage").Visibility==Visibility.Collapsed,"automatic sleep did not display resource page");}
 static void ExpectReset(StripWindow window,Action input,string name){window.WakeSleep();Age(window,61);input();window.EvaluateSleep();Check(!window.Sleeping,name+" did not restart idle timer");Check((DateTime.UtcNow-Field<DateTime>(window,"quietSince")).TotalSeconds<5,name+" did not record interaction");ExpectSleep(window);window.WakeSleep();}

 static void ActualMinute(StripWindow window){
  window.WakeSleep();window.Config.SleepAuto=true;window.Engine.Connected=true;window.Engine.Cards.Clear();
  for(int i=0;i<7;i++){var card=new Card{Id="earlier-"+i,Host="local",Title="Earlier task",Status="idle",Stale=true,Interaction=J.Now-86400};window.Engine.Cards[card.Key]=card;}
  typeof(StripWindow).GetField("quietSince",Private).SetValue(window,DateTime.MinValue);
  var frame=new DispatcherFrame();var watch=Stopwatch.StartNew();var refresh=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};var check=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
  refresh.Tick+=delegate{window.Render();};check.Tick+=delegate{if(window.Sleeping||watch.Elapsed.TotalSeconds>65)frame.Continue=false;};
  window.StartSleepWatcher();refresh.Start();check.Start();try{Dispatcher.PushFrame(frame);}finally{refresh.Stop();check.Stop();window.StopSleepWatcher();}
  Check(window.Sleeping&&watch.Elapsed.TotalSeconds>=59.9&&watch.Elapsed.TotalSeconds<66,"real one-minute timer failed with stale idle history and repeated rendering");
  Check(J.Str(J.Parse(File.ReadAllText(Path.Combine(Settings.DataDir,"sleep-state.json"))),"reason")=="sleeping","sleep diagnostic did not record actual timed entry");
  Console.WriteLine("PASS: actual timer entered sleep after "+watch.Elapsed.TotalSeconds.ToString("0.0")+" seconds with seven stale idle cards and four renders per second");
 }
 [STAThread] static int Main(string[] args){StripWindow window=null;try{
  Settings.DataDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"auto-sleep-test-data");
  new Settings{UiScale=1,DefaultUiScale=1,Width=1280,Height=400,ThemeMode="light",SleepAuto=true,SleepIdleMinutes=1,Topmost=false,TopmostMode="none",ReserveWorkArea=false}.Save();
  var app=new Application();window=new StripWindow();
  // A hidden handle supplies the keyboard source without starting live refresh.
  var source=HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
  window.Engine.Connected=true;
  var card=new Card{Id="sample",Host="local",Title="Example task",Status="completed",Interaction=2};window.Engine.Cards[card.Key]=card;
  window.ShowSettings();Check(window.PanelOpen,"settings did not open");
  Age(window,59);window.EvaluateSleep();Check(!window.Sleeping,"slept before one minute elapsed");
  var started=Field<DateTime>(window,"quietSince");for(int i=0;i<5;i++)window.Render();Check(Field<DateTime>(window,"quietSince")==started,"render refreshed idle timer");
  ExpectSleep(window);Check(window.TestSleepClick(),"click did not return from automatic sleep");

  var panel=Field<InsetPanel>(window,"activePanel");
  ExpectReset(window,()=>panel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseDownEvent}),"click");
  ExpectReset(window,()=>panel.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=UIElement.PreviewMouseWheelEvent}),"scroll");
  ExpectReset(window,()=>panel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.A){RoutedEvent=UIElement.PreviewKeyDownEvent}),"key");
  Age(window,61);panel.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.PreviewMouseMoveEvent});window.EvaluateSleep();Check(window.Sleeping,"pointer movement prevented sleep");window.WakeSleep();

  new UsageWindow(window).Show();ExpectSleep(window);window.WakeSleep();
  window.ShowPreview(card);ExpectSleep(window);window.WakeSleep();
  window.ClosePanel();ExpectSleep(window);window.WakeSleep();
  window.Config.Count=1;
  var historical=new Card{Id="historical",Host="local",Title="Earlier task",Status="notLoaded",Stale=true,Interaction=1};window.Engine.Cards[historical.Key]=historical;
  Check(Card.Sort(window.Engine.Cards.Values,window.Config)[0]==card,"historical test card was visible");
  Age(window,61);window.EvaluateSleep();Check(window.Sleeping,"unloaded historical card prevented automatic sleep");window.WakeSleep();
  historical.Status="active";Age(window,61);window.EvaluateSleep();Check(!window.Sleeping,"hidden stale active card allowed sleep");
  historical.Status="notLoaded";historical.QuestionPending=true;Age(window,61);window.EvaluateSleep();Check(!window.Sleeping,"hidden pending question allowed sleep");
  historical.QuestionPending=false;
  foreach(string state in new[]{"idle","completed","interrupted"}){historical.Status=state;Age(window,61);window.EvaluateSleep();Check(window.Sleeping,"stale historical "+state+" card prevented automatic sleep");window.WakeSleep();}
  historical.Status="completed";historical.Unread=true;Age(window,61);window.EvaluateSleep();Check(!window.Sleeping,"stale unread result allowed sleep");historical.Unread=false;
  historical.CompletionPending=true;Age(window,61);window.EvaluateSleep();Check(!window.Sleeping,"stale pending completion allowed sleep");historical.CompletionPending=false;
  window.Engine.Cards.Remove(historical.Key);ExpectSleep(window);
  window.Engine.Connected=false;window.EvaluateSleep();Check(window.Sleeping,"temporary disconnect dismissed resource page");window.Engine.Connected=true;
  card.Status="active";window.EvaluateSleep();Check(!window.Sleeping,"new task did not wake automatic sleep");
  card.Status="completed";card.Unread=true;Age(window,61);window.EvaluateSleep();Check(!window.Sleeping,"unread completion allowed sleep");card.Unread=false;
  card.QuestionPending=true;Age(window,61);window.EvaluateSleep();Check(!window.Sleeping,"pending question allowed sleep");card.QuestionPending=false;
  card.Stale=true;Age(window,61);window.EvaluateSleep();Check(window.Sleeping,"stale completed card prevented sleep");window.WakeSleep();card.Stale=false;
  window.Engine.Connected=false;Age(window,61);window.EvaluateSleep();Check(!window.Sleeping,"disconnected desktop allowed sleep");window.Engine.Connected=true;
  window.Config.SleepAuto=false;Age(window,61);window.EvaluateSleep();Check(!window.Sleeping,"disabled automatic sleep was ignored");
  card.Status="active";card.TurnId="turn-1";window.ShowSleepMonitor();card.Message="More progress";window.EvaluateSleep();Check(window.Sleeping,"ordinary progress woke manual sleep");
  card.QuestionPending=true;window.EvaluateSleep();Check(!window.Sleeping,"new question did not wake manual sleep");
  string status=File.ReadAllText(Path.Combine(Settings.DataDir,"sleep-state.json"));Check(J.Str(J.Parse(status),"reason")=="disabled","sleep status did not record its current reason");Check(!status.Contains("Example task")&&!status.Contains("historical"),"sleep status exposed task data");
  if(Array.IndexOf(args,"--realtime")>=0)ActualMinute(window);
  Console.WriteLine("PASS: idle settings/usage/preview; stale idle/completed/interrupted history; raw unread and pending completion; transient disconnect; timeout boundary; click/scroll/key restart; active and pending request wake; local sleep status");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(window!=null)window.Close();}}
}
