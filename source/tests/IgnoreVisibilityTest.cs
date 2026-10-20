using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using CodexStrip;

class IgnoreVisibilityTest {
 static int checks;
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static IEnumerable<T> Children<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static Grid Cards(StripWindow w){return (Grid)typeof(StripWindow).GetField("cards",Private).GetValue(w);}
 static string[] Titles(StripWindow w){return Cards(w).Children.OfType<Button>().Select(AutomationProperties.GetName).ToArray();}
 static void Logic(){
  var settings=new Settings{Count=2,SleepIgnoreKeywords=new List<string>{"hb"}};
  var hb=new Card{Id="hb",Host="remote-ssh-discovered:example",Title="Nightly HB monitor",Interaction=100};
  var cloud=new Card{Id="cloud",Host="durable",Title="Cloud task",Interaction=90};
  var a=new Card{Id="a",Host="local",Title="Regular task",Message="hb in message",Interaction=80};
  var b=new Card{Id="b",Host="remote-control:example",Title="Second regular task",Interaction=70};
  var all=new[]{hb,cloud,a,b};
  settings.Clicks[hb.Key]=1000;settings.Watched[hb.Id]=hb.Host;
  foreach(string status in new[]{"active","completed","idle","input","approval","reconnecting","failed","systemError","unknown"}){
   hb.Status=cloud.Status=status;hb.Stale=cloud.Stale=true;
   hb.Unread=cloud.Unread=true;hb.CompletionPending=cloud.CompletionPending=true;hb.QuestionPending=cloud.QuestionPending=true;
   Check(Card.Sort(all,settings).SequenceEqual(new[]{a,b}),"ignored status occupied a card slot: "+status);
   Check(hb.Unread&&hb.CompletionPending&&cloud.Unread&&cloud.CompletionPending,"hiding changed read state");
  }
  settings.Host=hb.Host;Check(Card.Sort(all,settings).Length==0,"host filter exposed ignored thread");
  settings.Host="all";settings.SleepIgnoreKeywords.Clear();Check(Card.Sort(all,settings).First()==hb,"clearing keyword did not restore card");
  hb.Title="Renamed task";settings.SleepIgnoreKeywords.Add("hb");Check(Card.Sort(all,settings).First()==hb,"rename out of rule stayed hidden");
  hb.Title="renamed-HB";Check(Card.Sort(all,settings).SequenceEqual(new[]{a,b}),"rename into rule stayed visible");
  settings.SleepIgnoreSources.Clear();Check(Card.Sort(all,settings).First()==cloud,"unchecking source did not restore card");
  settings.SleepIgnoreSources.Add("remote-control");Check(Card.Sort(all,settings).All(c=>c!=b),"remote-control source rule failed");
  settings.SleepIgnoreSources=new List<string>{"local","remote-ssh","remote-control","durable"};
  Check(Card.Sort(all,settings).Length==0,"all-ignored list was not empty");
  settings.Save();var loaded=Settings.Load();
  Check(Card.Sort(all,loaded).Length==0&&loaded.SleepIgnoreKeywords.SequenceEqual(new[]{"hb"}),"ignore visibility did not survive restart");
 }
 static async Task Settle(StripWindow w){await Task.Delay(350);w.UpdateLayout();}
 static async Task Visuals(StripWindow w,string output){
  await Settle(w);w.Engine.Cards.Clear();w.Engine.Connected=true;w.Config.Count=4;w.Config.SleepIgnoreSources.Clear();w.Config.SleepIgnoreKeywords.Clear();
  var hb=new Card{Id="hb",Host="remote-ssh-discovered:example",Title=UiText.T("总结近期训练与改进方向-hb","Training summary-hb"),Status="completed",Unread=true,Interaction=600,Message=UiText.T("监控已完成；这条线程应被 hb 规则隐藏。","Monitoring completed; this thread matches the hb rule.")};
  var cloud=new Card{Id="cloud",Host="durable",Title=UiText.T("云端监控任务","Cloud monitor"),Status="unknown",Stale=true,Interaction=590,Message=UiText.T("该来源已被忽略。","This source is ignored.")};
  w.Engine.Cards[hb.Key]=hb;w.Engine.Cards[cloud.Key]=cloud;
  string[] names={UiText.T("梳理本周组会内容","Prepare this week's meeting"),UiText.T("调研适合人脸复原的 GAN","Research face restoration GANs"),UiText.T("总结近期训练与改进方向","Review recent training"),UiText.T("整理实验结果","Collect experiment results")};
  for(int i=0;i<names.Length;i++){var c=new Card{Id="regular-"+i,Host="local",Title=names[i],Status=i==0?"completed":"active",Interaction=500-i,Message=UiText.T("普通线程继续显示，隐藏的线程不占卡片名额。","Regular threads remain visible and fill the available card slots.")};w.Engine.Cards[c.Key]=c;}
  w.Render();await Settle(w);w.Snapshot(Path.Combine(output,"ignore-before.png"));
  var editor=new SleepIgnoreEditor(w);var keyword=Children<TextBox>(editor).Single();keyword.Text="hb";
  Children<Button>(editor).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  Check(!Titles(w).Any(t=>t.Contains("-hb"))&&Titles(w).Length==4,"keyword Apply did not immediately refill visible cards");
  var source=Children<CheckBox>(editor).Single(c=>AutomationProperties.GetAutomationId(c)=="SleepIgnoreSource-durable");source.IsChecked=true;
  Check(Titles(w).Length==4&&names.All(n=>Titles(w).Any(t=>t.StartsWith(n))),"source toggle did not immediately hide alerts and refill slots");
  await Settle(w);w.Snapshot(Path.Combine(output,"ignore-after.png"));
  source.IsChecked=false;Check(Titles(w).Any(t=>t.StartsWith(cloud.Title)),"source toggle did not restore the card immediately");
  keyword.Text="";Children<Button>(editor).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  Check(Titles(w).Any(t=>t.StartsWith(hb.Title)),"clearing keywords did not restore card immediately");
  w.ShowPreview(hb);await Settle(w);w.Config.SleepIgnoreKeywords.Add("hb");w.Render();await Settle(w);
  Check(!w.PanelOpen,"newly ignored conversation remained in the open preview");
  w.Config.SleepIgnoreSources.Add("durable");w.Render();await Settle(w);
  w.Width=480;w.Height=900;await Settle(w);w.Snapshot(Path.Combine(output,"ignore-after-portrait.png"));
  Check(Titles(w).Length==4&&!Titles(w).Any(t=>t.Contains("-hb")||t.StartsWith(cloud.Title)),"portrait exposed ignored cards");
  w.Width=1600;w.Height=700;w.ShowSettings();await Settle(w);
  var panel=(DependencyObject)typeof(StripWindow).GetField("activePanel",Private).GetValue(w);
  var hint=Children<TextBlock>(panel).Single(t=>t.Text==UiText.T("勾选来源的线程会隐藏，不显示完成、未读或待同步提示，也不影响静息和唤醒。","Selected sources are hidden, including completion, unread and sync alerts. They do not affect idle timing or wake."));
  hint.BringIntoView();await Settle(w);w.Snapshot(Path.Combine(output,"ignore-settings.png"));
  w.ClosePanel();w.Config.SleepIgnoreSources=SleepDiagnostic.SourceIds.ToList();w.Render();await Settle(w);
  Check(Titles(w).Length==0,"empty state still rendered an ignored card");w.Snapshot(Path.Combine(output,"ignore-all-hidden.png"));
 }
 [STAThread]static int Main(string[] args){StripWindow w=null;try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Settings.DataDir=Path.Combine(output,"data");Logic();
  new Settings{UiScale=1,DefaultUiScale=1,Width=1600,Height=375,ThemeMode="light",Topmost=false,TopmostMode="none",ReserveWorkArea=false,SleepAuto=false}.Save();
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};w=new StripWindow(true);w.Left=-30000;w.Top=-30000;app.MainWindow=w;Exception failure=null;
  w.Loaded+=async delegate{try{await Visuals(w,output);}catch(Exception e){failure=e;}finally{w.Close();app.Shutdown();}};
  w.Show();app.Run();if(failure!=null)throw failure;
  Console.WriteLine("PASS: "+checks+" visibility, refill, live editor, preview, persistence and layout checks");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(w!=null)w.Close();}}
}
