using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CodexStrip;

// Compile against the language-specific executable, then run with
// --output <isolated QA directory> --language zh-CN|en.
// This uses demo resources and never opens a live desktop IPC connection.
class LanguageUiTest {
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static int result=1;
 static string output;
 static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,Private).GetValue(instance);}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject {
  if(root is T)yield return (T)root;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
 }
 static string[] Texts(DependencyObject root){return Children<TextBlock>(root).Select(t=>t.Text).Concat(Children<Button>(root).Where(b=>b.Content is string).Select(b=>(string)b.Content)).ToArray();}
 static void Label(DependencyObject root,string chinese,string english){
  string expected=UiText.T(chinese,english),other=UiText.English?chinese:english;
  var texts=Texts(root);
  Check(texts.Contains(expected),"Missing localized label: "+expected);
  if(chinese!=english)Check(!texts.Contains(other),"Wrong-language built-in label: "+other);
 }
 static InsetPanel Panel(StripWindow w){return Field<InsetPanel>(w,"activePanel");}
 static ScrollViewer Scroll(InsetPanel panel){return (ScrollViewer)typeof(InsetPanel).GetField("bodyScroll",Private).GetValue(panel);}
 static Button Choice(StripWindow w,string chinese,string english){
  string title=UiText.T(chinese,english);
  var matches=Children<Button>(Panel(w)).Where(b=>object.Equals(b.Content,title)).ToArray();
  Check(matches.Length==1,"Expected one settings choice: "+title+", found "+matches.Length);
  return matches[0];
 }
 static async Task Click(StripWindow w,string chinese,string english){Choice(w,chinese,english).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(w);}
 static async Task Settle(StripWindow w){await Task.Delay(400);w.UpdateLayout();}
 static void Contained(FrameworkElement item,FrameworkElement outer,string name){
  Check(item.ActualWidth>0&&item.ActualHeight>0,name+" has no usable area");
  Rect bounds=item.TransformToAncestor(outer).TransformBounds(new Rect(0,0,item.ActualWidth,item.ActualHeight));
  Check(bounds.Left>=-2&&bounds.Top>=-2&&bounds.Right<=outer.ActualWidth+2&&bounds.Bottom<=outer.ActualHeight+2,
   name+" outside "+outer.ActualWidth+"x"+outer.ActualHeight+": "+bounds);
 }
 static void Bounds(StripWindow w){
  w.UpdateLayout();
  Check(Application.Current.Windows.Count==1,"A utility opened another native window");
  var panel=Panel(w);
  if(panel!=null){
   Contained(Field<Border>(w,"panelLayer"),w,"Utility container");
   Contained(panel,w,"Utility panel");
   Contained(Scroll(panel),panel,"Utility scroll viewport");
   var close=Children<Button>(panel).Single(b=>AutomationProperties.GetName(b)==UiText.T("返回看板","Back to dashboard"));
   Contained(close,panel,"Utility close button");
  }
  foreach(string field in new[]{"quota","settingsButton","fullscreenButton","dockButton"})Contained(Field<Button>(w,field),w,field);
  var quota=Field<Button>(w,"quota");
  var title=Field<TextBlock>(w,"brandTitle");
  var logo=((StackPanel)title.Parent).Children.OfType<Image>().Single();
  Contained(logo,w,"Brand logo");
  double actionsLeft=quota.TransformToAncestor(w).Transform(new Point(0,0)).X;
  foreach(var brand in new FrameworkElement[]{logo,title}.Where(b=>b.Visibility==Visibility.Visible)){
   Rect bounds=brand.TransformToAncestor(w).TransformBounds(new Rect(0,0,brand.ActualWidth,brand.ActualHeight));
   Check(bounds.Right<=actionsLeft+1,"Brand overlaps the quota button: "+bounds);
  }
  foreach(var button in ((StackPanel)quota.Parent).Children.OfType<Button>())Contained(button,w,"Header button");
 }
 static void Snapshot(StripWindow w,string name){Bounds(w);w.Snapshot(Path.Combine(output,name+".png"));}
 static void CardContent(StripWindow w){
  var grid=Field<Grid>(w,"cards");
  foreach(Card card in w.Engine.Cards.Values){
   string automation=card.Title+UiText.T("，",", ");
   var button=grid.Children.OfType<Button>().Single(b=>AutomationProperties.GetName(b).StartsWith(automation,StringComparison.Ordinal));
   var text=Children<TextBlock>(button).Select(t=>t.Text).ToArray();
   Check(text.Contains(card.Title),"Card title changed: "+card.Title);
   Check(text.Contains(card.Message),"Card message changed: "+card.Message);
   // Scope status checks to the metadata row: raw titles can equal a status.
   var metadata=(DockPanel)((Grid)button.Content).Children[0];
   switch(card.Status){
    case "active":Label(metadata,"进行中","Running");break;
    case "completed":Label(metadata,"已完成","Completed");break;
    case "approval":Label(metadata,"需要确认","Approval needed");break;
    case "input":Label(metadata,"等待回答","Awaiting input");break;
   }
  }
 }
 static void SettingsLabels(StripWindow w){
  foreach(var pair in new[]{
   new[]{"看板设置","Dashboard settings"},new[]{"休眠监控","Idle monitor"},new[]{"显示","Display"},new[]{"屏幕停靠","Docking"},
   new[]{"位置","Position"},new[]{"浮动","Floating"},new[]{"顶部","Top"},new[]{"左侧","Left"},new[]{"右侧","Right"},
   new[]{"主机","Host"},new[]{"全部","All"},new[]{"本机","Local"},new[]{"跟随系统","System"},new[]{"白天","Light"},new[]{"夜晚","Dark"},
   new[]{"立即进入静息","Open resource monitor"},new[]{"自动休眠","Auto idle"},new[]{"静息时间","Idle delay"}})Label(Panel(w),pair[0],pair[1]);
 }
 static void UsageLabels(StripWindow w){
  foreach(var pair in new[]{new[]{"账户额度","Account usage"},new[]{"周额度剩余 · 演示数据","Weekly left · Demo"},new[]{"刷新","Refresh"}})Label(Panel(w),pair[0],pair[1]);
  Check(Texts(Panel(w)).Any(t=>t.StartsWith(UiText.T("下次重置  ","Next reset  "),StringComparison.Ordinal)),"Reset label not localized");
 }
 static void SleepLabels(StripWindow w){
  var layer=Field<Grid>(w,"sleepLayer");
  Check(w.Sleeping&&layer.Visibility==Visibility.Visible,"Resource monitor did not open");
  Check(Field<Border>(w,"taskPage").Visibility==Visibility.Collapsed&&Math.Abs(layer.Opacity-1)<.001,"Resource monitor snapshot captured a page transition");
  Contained(layer,w,"Resource monitor");
  foreach(var pair in new[]{new[]{"静息监控 · 演示","Idle monitor · Demo"},new[]{"内存","Memory"},new[]{"网络","Network"},new[]{"磁盘","Disk"}})Label(layer,pair[0],pair[1]);
  if(!w.Portrait)Label(layer,"返回任务  ↗","Back to tasks  ↗");
  foreach(var text in Children<TextBlock>(layer).Where(t=>t.Text==UiText.T("静息监控 · 演示","Idle monitor · Demo")))Contained(text,layer,"Resource monitor heading");
 }
 static async Task ScrollWorks(StripWindow w,string name){
  var scroll=Scroll(Panel(w));
  Check(scroll.ScrollableHeight>0,name+" content cannot scroll");
  Check(scroll.ComputedVerticalScrollBarVisibility!=Visibility.Visible,name+" uses a native vertical scrollbar");
  scroll.ScrollToEnd();await Settle(w);
  Check(scroll.VerticalOffset>0&&Math.Abs(scroll.VerticalOffset-scroll.ScrollableHeight)<2,name+" cannot reach the end");
  string position=UiText.T("滚动位置 ","Scroll position ")+"100%";
  Check(Children<Grid>(Panel(w)).Any(g=>AutomationProperties.GetName(g)==position),name+" has no localized scroll-position control");
  Snapshot(w,name+"-bottom");
  scroll.ScrollToTop();await Settle(w);
  Check(scroll.VerticalOffset<1,name+" cannot return to the top");
 }
 static void PersistedChoices(StripWindow w){
  // Selecting translated labels must still serialize the shared machine values.
  w.Config.Save();var saved=J.Parse(File.ReadAllText(Path.Combine(Settings.DataDir,"settings.json")));
  foreach(var pair in new[]{new[]{"theme","dark"},new[]{"host","local"},new[]{"dockEdge","right"},new[]{"sleepSpeedUnit","mbps"},new[]{"sleepAccent","purple"}})
   Check(J.Str(saved,pair[0])==pair[1],"Settings UI changed config key/value: "+pair[0]);
  Check(J.Num(saved,"sleepIdleMinutes")==3,"Idle-delay selection did not save the language-neutral value");
  Check(object.Equals(J.Get(saved,"sleepGlassRefraction"),true)&&object.Equals(J.Get(saved,"sleepRingCharts"),false)&&object.Equals(J.Get(saved,"sleepSmoothRings"),false),"Monitor choices did not persist their boolean values");
  var reloaded=Settings.Load();
  Check(reloaded.Host=="local"&&reloaded.DockEdge=="right"&&reloaded.ThemeMode=="dark"&&reloaded.SleepSpeedUnit=="mbps"&&reloaded.SleepAccent=="purple"&&reloaded.SleepIdleMinutes==3,"Saved settings did not reload in this language build");
  Check(reloaded.SleepWidgets.SequenceEqual(Settings.SleepWidgetIds),"Localized metric names changed widget IDs");
 }
 static async Task Run(StripWindow w){
  await Settle(w);
  Check(w.DemoMode&&w.Engine.Stream==null&&w.Engine.Bridge.PipeName==null,"Test attempted to use live desktop data");
  var examples=new[]{
   new Card{Id="raw-settings",Host="local",Title="设置",Message="最新回复",Status="active",Interaction=J.Now},
   new Card{Id="raw-connection",Host="local",Title="正在连接 Codex…",Message="最新进展",Status="completed",Interaction=J.Now-1},
   new Card{Id="raw-state",Host="local",Title="已完成",Message="进展",Status="approval",Interaction=J.Now-2},
   new Card{Id="raw-account",Host="local",Title="账户额度",Message="实时对话",Status="input",Interaction=J.Now-3}};
  w.Engine.Cards.Clear();foreach(var card in examples)w.Engine.Cards[card.Key]=card;w.Render();await Settle(w);
  CardContent(w);Snapshot(w,"dashboard-wide");
  examples[1].Unread=true;w.Render();await Settle(w);Label(Field<Grid>(w,"cards"),"完成 · 未查看","Done · Unread");examples[1].Unread=false;w.Render();

  w.ShowSettings();await Settle(w);SettingsLabels(w);Snapshot(w,"settings-wide");
  Check(((Grid)Scroll(Panel(w)).Content).ColumnDefinitions.Count==2,"Wide settings lost their two-column layout");
  await Click(w,"本机","Local");await Click(w,"夜晚","Dark");await Click(w,"右侧","Right");await Click(w,"3分","3 min");
  await Click(w,"自定义项目、壁纸和颜色  ↓","Customize monitor  ↓");
  await Click(w,"折射","Refraction");await Click(w,"条形","Bars");await Click(w,"即时","Instant");await Click(w,"Mb/s","Mb/s");await Click(w,"紫","Purple");
  PersistedChoices(w);Snapshot(w,"settings-wide-dark");
  // Restore light/rings for consistent QA screenshots after persistence checks.
  w.Config.SleepRingCharts=true;w.Config.SleepSmoothRings=true;w.Config.SleepGlassRefraction=false;w.Config.SleepAccent="blue";w.Config.SleepSpeedUnit="auto";w.SetTheme("light");await Settle(w);
  w.ShowUsage();await Settle(w);UsageLabels(w);Snapshot(w,"usage-wide");
  w.ClosePanel();await Settle(w);w.ShowSleepDemo();await Task.Delay(850);await Settle(w);SleepLabels(w);w.Snapshot(Path.Combine(output,"idle-wide.png"));w.WakeSleep();await Settle(w);

  w.Width=900;w.Height=286;await Settle(w);w.ShowSettings();await Settle(w);SettingsLabels(w);Snapshot(w,"settings-narrow");await ScrollWorks(w,"settings-narrow");
  w.ClosePanel();await Settle(w);
  string originalMessage=examples[0].Message;
  examples[0].Message=string.Join("\n",Enumerable.Repeat("最新回复\n设置\n正在连接 Codex…",25));
  w.ShowPreview(examples[0]);await Settle(w);await ScrollWorks(w,"preview-narrow");Snapshot(w,"preview-narrow");
  examples[0].Message=originalMessage;
  w.ClosePanel();await Settle(w);w.Width=420;w.Height=1000;await Settle(w);w.Render();await Settle(w);CardContent(w);Snapshot(w,"dashboard-portrait");
  w.ShowSettings();await Settle(w);SettingsLabels(w);Snapshot(w,"settings-portrait");await ScrollWorks(w,"settings-portrait");
  w.ShowUsage();await Settle(w);UsageLabels(w);Snapshot(w,"usage-portrait");
  w.ClosePanel();await Settle(w);w.ShowSleepDemo();await Task.Delay(850);await Settle(w);SleepLabels(w);w.Snapshot(Path.Combine(output,"idle-portrait.png"));w.WakeSleep();await Settle(w);

  w.Width=320;await Settle(w);w.Render();await Settle(w);Snapshot(w,"dashboard-minimum");
  w.Width=420;await Settle(w);

  // A raw message that happens to match system words must remain untouched in preview too.
  examples[0].Message=string.Join("\n",Enumerable.Repeat("最新回复\n设置\n正在连接 Codex…",25));
  w.ShowPreview(examples[0]);await Settle(w);Bounds(w);
  Check(Texts(Panel(w)).Contains("LOCAL  ·  设置"),"Preview translated the raw conversation title");
  var reader=Children<RichTextBox>(Panel(w)).Single();
  string preview=new TextRange(reader.Document.ContentStart,reader.Document.ContentEnd).Text.Replace("\r","").TrimEnd('\n');
  Check(preview==examples[0].Message,"Preview translated or changed the raw conversation message");
  await ScrollWorks(w,"preview-portrait");Snapshot(w,"preview-portrait");
  w.SetTheme("dark");await Settle(w);Bounds(w);
  reader=Children<RichTextBox>(Panel(w)).Single();
  Check(new TextRange(reader.Document.ContentStart,reader.Document.ContentEnd).Text.Replace("\r","").TrimEnd('\n')==examples[0].Message,"Theme rebuild changed raw message language");
  Snapshot(w,"preview-portrait-dark");
 }
 static string Argument(string[] args,string key){int index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:"";}
 [STAThread]static int Main(string[] args){
  StripWindow w=null;string priorDataDir=Settings.DataDir;
  try{
   string language=Argument(args,"--language");Check(language=="en"||language=="zh-CN","Pass --language en or zh-CN to verify the selected build");
   Check(UiText.English==(language=="en"),"Executable language does not match the requested package");
   output=Path.GetFullPath(Argument(args,"--output"));Check(Argument(args,"--output").Length>0,"Pass an isolated --output directory");Directory.CreateDirectory(output);
   Settings.DataDir=Path.Combine(output,"test-data-"+Guid.NewGuid().ToString("N"));
   new Settings{UiScale=1,DefaultUiScale=1,Width=2300,Height=420,ThemeMode="light",SleepAuto=false,Topmost=false,TopmostMode="none",ReserveWorkArea=false,Count=4,Left=40,Top=40}.Save();
   var app=new Application();w=new StripWindow(true);app.MainWindow=w;
   w.Loaded+=async delegate{
    try{await Run(w);result=0;File.WriteAllText(Path.Combine(output,"language-ui-test.txt"),"PASS ("+language+"): built-in labels and status; raw Chinese task title/message preserved; settings choices retain shared config keys and values; wide/narrow/portrait utility bounds and scrolling; localized resource monitor; QA snapshots");}
    catch(Exception e){File.WriteAllText(Path.Combine(output,"language-ui-test.txt"),e.ToString());Console.Error.WriteLine(e);}
    finally{w.Close();}
   };
   app.Run(w);return result;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
  finally{if(w!=null&&w.IsVisible)w.Close();Settings.DataDir=priorDataDir;}
 }
}
