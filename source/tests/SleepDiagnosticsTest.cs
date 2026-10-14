using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexStrip;

class SleepDiagnosticsTest {
 static int checks;
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static T Field<T>(StripWindow w,string name){return (T)typeof(StripWindow).GetField(name,Private).GetValue(w);}
 static void Age(StripWindow w,int seconds){typeof(StripWindow).GetField("quietSince",Private).SetValue(w,DateTime.UtcNow.AddSeconds(-seconds));}
 static IEnumerable<T> Children<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static SleepDiagnostic Diagnostic(Card[] cards,DateTime since,DateTime now,IEnumerable<string> keywords=null){return SleepDiagnostic.Create(true,cards,true,false,false,since,now,3,keywords);}
 static void Logic(){
  var now=new DateTime(2026,10,14,12,0,0,DateTimeKind.Utc);var quiet=new Card{Id="quiet",Host="local",Title="Finished task",Status="completed"};
  foreach(string state in new[]{"completed","idle","notLoaded","interrupted","failed"}){
   quiet.Status=state;Check(SleepPolicy.Quiet(true,new[]{quiet}),state+" changed quiet policy");Check(Diagnostic(new[]{quiet},now,now).Blockers.Length==0,state+" became a diagnostic blocker");
  }
  foreach(string state in new[]{"active","reconnecting","input","approval","systemError","unknown",null}){
   var c=quiet.Copy();c.Status=state;var d=Diagnostic(new[]{c},now,now);Check(!SleepPolicy.Quiet(true,new[]{c})&&d.Blockers.Length==1&&!d.RemainingSeconds.HasValue,"missing blocker for "+state);
  }
  quiet.Status="completed";quiet.Unread=true;quiet.CompletionPending=true;quiet.QuestionPending=true;
  Check(Diagnostic(new[]{quiet},now,now).Blockers.Single().Reasons.Length==3,"overlapping reasons were lost");
  quiet.Unread=false;quiet.CompletionPending=false;quiet.QuestionPending=false;
  Check(Diagnostic(new[]{quiet},now.AddSeconds(-100),now).RemainingSeconds==80,"wrong countdown");
  Check(Diagnostic(new[]{quiet},now.AddSeconds(-100),now.AddSeconds(1)).RemainingSeconds==79,"countdown did not tick");
  Check(Diagnostic(new[]{quiet},now.AddSeconds(-179.1),now).RemainingSeconds==1,"countdown rounded down early");
  Check(Diagnostic(new[]{quiet},now.AddSeconds(-180),now).RemainingSeconds==0,"deadline countdown is nonzero");
  Check(Diagnostic(new[]{quiet},now.AddMinutes(-10),now).RemainingSeconds==0,"negative countdown");
  Check(Diagnostic(new[]{quiet},now.AddMinutes(1),now).RemainingSeconds==180,"future timestamp increased countdown beyond delay");
  Check(Diagnostic(new[]{quiet},DateTime.MinValue,now).RemainingSeconds==180,"new timer was not full delay");
  Check(SleepDiagnostic.FormatCountdown(3600)=="60:00","60-minute countdown wrapped");
  Check(!SleepDiagnostic.Create(false,new[]{quiet},true,false,false,now,now,3).RemainingSeconds.HasValue,"disconnect kept counting");
  Check(SleepDiagnostic.Create(true,new[]{quiet},false,false,false,now,now,3).Reason=="disabled","disabled reason missing");
  Check(SleepDiagnostic.Create(true,new[]{quiet},true,true,true,now,now,3).Reason=="manual_sleep","manual sleep reason missing");
  var hb=new Card{Id="hb-id",Host="remote-ssh-discovered:example",Title="Nightly HB monitor",Status="active",Stale=true};
  var keywords=SleepDiagnostic.ParseKeywords(" hb, HB；heartbeat\n ， ; ");Check(keywords.SequenceEqual(new[]{"hb","heartbeat"}),"keyword parse/dedup failed");
  Check(SleepPolicy.Quiet(true,new[]{hb},keywords),"case-insensitive title keyword did not allow sleep");
  Check(Diagnostic(new[]{hb},now,now,keywords).Ignored.Single().Keyword=="hb","ignored match was not visible");
  hb.Message="hb";hb.Title="Regular task";Check(!SleepPolicy.Quiet(true,new[]{hb},keywords),"message content was matched as title");
  Check(!SleepPolicy.Quiet(true,new[]{hb},new[]{""," "}),"blank keyword matched every title");
  hb.Title=null;Check(!SleepPolicy.Quiet(true,new[]{hb},keywords),"null title was ignored");
  hb.Title="HB nightly";hb.Status="completed";hb.Unread=true;hb.CompletionPending=true;hb.QuestionPending=true;
  Check(SleepPolicy.Quiet(true,new[]{hb},keywords),"ignored attention still blocked sleep");Check(hb.Unread&&hb.CompletionPending&&hb.QuestionPending,"ignore mutated attention state");
  var foreign=new Card{Id="hidden",Host="durable",Title="Hidden blocker",Status="approval"};var both=Diagnostic(new[]{hb,foreign},now,now,keywords);
  Check(both.Blockers.Length==1&&both.Blockers[0].Card==foreign&&both.Ignored.Length==1,"ignored thread hid unrelated blocker");
  var failed=new Card{Id="ended-failure",Host="remote-ssh-discovered:example",Title="Ended task"};
  failed.Snapshot(J.Obj("thread",J.Obj("status",J.Obj("type","idle")),"latestTurn",J.Obj("id","failed-turn","status","failed","error",J.Obj("message","stream disconnected before completion"))));
  Check(failed.Status=="failed"&&failed.LastError.Length>0,"failed-turn fixture did not retain its error");Check(SleepPolicy.Quiet(true,new[]{failed})&&Diagnostic(new[]{failed},now,now).RemainingSeconds==180,"ended failed turn still blocked countdown");
  failed.Stale=true;Check(SleepPolicy.Quiet(true,new[]{failed}),"stale terminal failure prevented sleep");failed.QuestionPending=true;Check(!SleepPolicy.Quiet(true,new[]{failed}),"explicit pending question was suppressed by failure");failed.QuestionPending=false;
  var w=new StripWindow();try{
   Check(!w.Config.SleepDebug,"debug enabled by default");w.ShowSleepDebug();Check(!w.PanelOpen,"disabled Debug could still open");
   w.SetSleepDebugEnabled(true);Check(Settings.Load().SleepDebug,"debug enable did not persist");w.ShowSleepDebug();Check(w.PanelOpen,"enabled Debug did not open");w.SetSleepDebugEnabled(false);Check(!w.PanelOpen&&!Settings.Load().SleepDebug,"debug disable did not close panel or persist");
   w.Config.Count=1;w.Config.Host="local";w.Config.SleepAuto=true;w.Config.SleepIdleMinutes=1;w.Config.SleepIgnoreKeywords=keywords;w.Engine.Connected=true;
   hb.Status="active";hb.TurnId="one";w.Engine.Cards[hb.Key]=hb;w.Engine.Cards[quiet.Key]=quiet;
   w.WakeSleep();Age(w,61);w.EvaluateSleep();Check(w.Sleeping,"ignored hidden remote thread prevented automatic sleep");
   hb.TurnId="two";w.EvaluateSleep();Check(w.Sleeping,"ignored thread woke automatic sleep");
   hb.Title="Actual work";w.EvaluateSleep();Check(!w.Sleeping,"nonignored activity did not wake");
   var d=w.CurrentSleepDiagnostic();Check(d.Blockers.Length==1&&d.Blockers[0].Card==hb,"host filter hid blocker");
   hb.Title="HB nightly";w.ShowSleepMonitor();hb.TurnId="three";w.EvaluateSleep();Check(w.Sleeping,"ignored thread woke manual sleep");
   w.WakeSleep();w.Config.SleepIgnoreKeywords.Clear();w.EvaluateSleep();Check(!w.CurrentSleepDiagnostic().RemainingSeconds.HasValue,"clear rules did not restore blocker");
   hb.Status="idle";hb.QuestionPending=false;hb.Unread=false;hb.CompletionPending=false;w.WakeSleep();Age(w,25);w.EvaluateSleep();var since=Field<DateTime>(w,"quietSince");for(int i=0;i<5;i++)w.Render();
   Check(Field<DateTime>(w,"quietSince")==since,"render reset countdown");Check(w.CurrentSleepDiagnostic().RemainingSeconds<=35,"countdown ignored elapsed time");
   typeof(StripWindow).GetMethod("RecordSleepInteraction",Private).Invoke(w,new object[0]);w.EvaluateSleep();Check(w.CurrentSleepDiagnostic().RemainingSeconds==60,"interaction did not reset countdown");
   var saved=File.ReadAllText(Path.Combine(Settings.DataDir,"sleep-state.json"));Check(J.Num(J.Parse(saved),"remainingSeconds")==60,"local diagnostic did not update seconds");Check(!saved.Contains(hb.Id)&&!saved.Contains(hb.Title),"local diagnostic saved conversation details");
   w.Engine.Cards[failed.Key]=failed;Age(w,61);w.EvaluateSleep();Check(w.Sleeping&&failed.Status=="failed"&&failed.LastError.Length>0,"ended error blocked sleep or was erased");w.WakeSleep();
   w.Config.SleepIgnoreKeywords=SleepDiagnostic.ParseKeywords("hb, timer");w.Save();Check(Settings.Load().SleepIgnoreKeywords.SequenceEqual(new[]{"hb","timer"}),"ignore rules did not survive reload");
  }finally{w.Close();}
 }
 static async Task Settle(StripWindow w){await Task.Delay(380);w.UpdateLayout();}
 static void Bounds(FrameworkElement item,FrameworkElement outer,string label){var bounds=item.TransformToAncestor(outer).TransformBounds(new Rect(0,0,item.ActualWidth,item.ActualHeight));Check(item.ActualWidth>0&&item.ActualHeight>0&&bounds.Left>=-1&&bounds.Top>=-1&&bounds.Right<=outer.ActualWidth+1&&bounds.Bottom<=outer.ActualHeight+1,label+" clipped: "+bounds);}
 static async Task Visuals(StripWindow w,string output){
  await Settle(w);w.Engine.Cards.Clear();w.Config.SleepAuto=true;w.Config.SleepIdleMinutes=3;w.Config.SleepIgnoreKeywords=new List<string>{"hb"};
  var card=new Card{Id="example-running",Host="local",Title=UiText.T("模型评测与结果整理","Evaluate model and collect results"),Status="active"};w.Engine.Cards[card.Key]=card;
  var hidden=new Card{Id="example-hidden",Host="durable",Title=UiText.T("远端环境配置","Configure remote environment"),Status="approval",Stale=true};w.Engine.Cards[hidden.Key]=hidden;
  var hb=new Card{Id="example-hb",Host="local",Title="hb · monitor",Status="active"};w.Engine.Cards[hb.Key]=hb;
  w.Render();await Settle(w);Check(!w.Config.SleepDebug&&Field<FrameworkElement>(w,"idleStatusRow").Visibility==Visibility.Collapsed&&Field<RowDefinition>(w,"idleStatusRowDefinition").Height.Value==0,"debug was visible on default dashboard");
  double normalHeight=Field<Grid>(w,"bodyGrid").ActualHeight;w.Snapshot(Path.Combine(output,"debug-off-dashboard.png"));w.ShowSettings();await Settle(w);
  var settings=Field<InsetPanel>(w,"activePanel");var toggleRow=Children<Grid>(settings).Single(g=>g.Children.OfType<TextBlock>().Any(t=>t.Text==UiText.T("静息调试","Idle Debug")));
  Children<Button>(toggleRow).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(w);
  Check(w.Config.SleepDebug&&Field<FrameworkElement>(w,"idleStatusRow").Visibility==Visibility.Visible,"settings toggle did not enable Debug");Check(Math.Abs(normalHeight-Field<Grid>(w,"bodyGrid").ActualHeight-24)<1,"debug row did not reserve its exact height");w.Snapshot(Path.Combine(output,"debug-enabled-settings.png"));
  w.ClosePanel();w.ShowSleepDebug();await Settle(w);Check(w.CurrentSleepDiagnostic().Blockers.Length==2,"debug lost hidden blocker");
  var panel=Field<InsetPanel>(w,"activePanel");Bounds(panel,w,"wide Debug panel");Bounds(Field<TextBlock>(w,"idleStatus"),w,"wide countdown");
  var input=Children<TextBox>(panel).Single();input.Text="hb，scheduler";Children<Button>(panel).Single(b=>object.Equals(b.Content,UiText.T("应用","Apply"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  Check(w.Config.SleepIgnoreKeywords.SequenceEqual(new[]{"hb","scheduler"}),"Apply did not update ignore rules");Check(input.Text=="hb, scheduler","Apply did not normalize input");
  var first=Children<TextBox>(panel).Single();for(int i=0;i<5;i++)w.EvaluateSleep();Check(object.ReferenceEquals(first,Children<TextBox>(panel).Single()),"automatic refresh rebuilt keyword editor");
  w.Snapshot(Path.Combine(output,"debug-blockers-wide.png"));
  card.Status="completed";hidden.Status="idle";Age(w,45);w.EvaluateSleep();var start=Field<DateTime>(w,"quietSince");var before=w.CurrentSleepDiagnostic().RemainingSeconds;
  await Task.Delay(1250);Check(w.CurrentSleepDiagnostic().RemainingSeconds<before,"real UI countdown did not tick");Check(Field<DateTime>(w,"quietSince")==start,"UI updates reset timer");
  await Settle(w);w.Snapshot(Path.Combine(output,"debug-countdown-wide.png"));
  w.Width=480;w.Height=900;await Settle(w);Bounds(Field<InsetPanel>(w,"activePanel"),w,"portrait Debug panel");Bounds(Field<TextBlock>(w,"idleStatus"),w,"portrait countdown");w.Snapshot(Path.Combine(output,"debug-countdown-portrait.png"));
  w.SetTheme("dark");await Settle(w);w.Snapshot(Path.Combine(output,"debug-countdown-dark.png"));w.SetTheme("light");
  w.Width=480;w.Height=315;w.Config.UiScale=1.5;w.ApplyTheme();await Settle(w);Bounds(Field<InsetPanel>(w,"activePanel"),w,"minimum Debug panel");Bounds(Field<TextBlock>(w,"idleStatus"),w,"minimum countdown");w.Snapshot(Path.Combine(output,"debug-minimum.png"));
  w.SetSleepDebugEnabled(false);await Settle(w);Check(!w.PanelOpen&&Field<FrameworkElement>(w,"idleStatusRow").Visibility==Visibility.Collapsed&&Field<RowDefinition>(w,"idleStatusRowDefinition").Height.Value==0,"disabling Debug left its panel or row visible");w.Snapshot(Path.Combine(output,"debug-off-minimum.png"));
  w.ShowSettings();await Settle(w);Check(!Children<Button>(Field<InsetPanel>(w,"activePanel")).Any(b=>object.Equals(b.Content,UiText.T("查看静息 Debug","Open idle Debug"))),"disabled Debug retained its settings entry");w.Snapshot(Path.Combine(output,"debug-disabled-settings.png"));
 }
 [STAThread]static int Main(string[] args){StripWindow w=null;try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Settings.DataDir=Path.Combine(output,"data");
  new Settings{UiScale=1,DefaultUiScale=1,Width=1600,Height=450,ThemeMode="light",SleepAuto=true,Topmost=false,TopmostMode="none",ReserveWorkArea=false}.Save();
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Logic();w=new StripWindow(true);app.MainWindow=w;int result=1;
  w.Loaded+=async delegate{try{await Visuals(w,output);Console.WriteLine("PASS: "+checks+" sleep diagnostics, countdown, keyword ignore, persistence, wake, privacy and UI checks");result=0;}catch(Exception e){Console.Error.WriteLine(e);}finally{w.Close();app.Shutdown();}};
  app.Run(w);return result;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(w!=null)w.Close();}}
}
