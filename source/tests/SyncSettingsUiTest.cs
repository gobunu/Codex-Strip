using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using CodexStrip;
class SyncSettingsUiTest {
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static IEnumerable<T> Children<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static InsetPanel Panel(StripWindow w){return (InsetPanel)typeof(StripWindow).GetField("activePanel",Private).GetValue(w);}
 static Button Choice(StripWindow w,string id){w.UpdateLayout();return Children<Button>(Panel(w)).Single(b=>AutomationProperties.GetAutomationId(b)==id);}
 static async Task Show(StripWindow w,bool baseline,string id="SyncRecentLimit-20"){
  var panel=Panel(w);var scroll=(ScrollViewer)typeof(InsetPanel).GetField("bodyScroll",Private).GetValue(panel);scroll.UpdateLayout();
  FrameworkElement anchor=baseline?(FrameworkElement)Children<TextBlock>(panel).First(t=>t.Text==UiText.T("界面倍率","UI scale")):Choice(w,id);
  Point point=anchor.TranslatePoint(new Point(0,0),(UIElement)scroll.Content);double margin=id.StartsWith("SyncRecentHours",StringComparison.Ordinal)&&!UiText.English?5:35;scroll.ScrollToVerticalOffset(Math.Max(0,point.Y-margin));await Task.Delay(180);w.UpdateLayout();
  if(!baseline)foreach(var button in Children<Button>(panel).Where(b=>AutomationProperties.GetAutomationId(b).StartsWith("SyncRecent",StringComparison.Ordinal))){
   if(button.ActualWidth>scroll.ViewportWidth+1)throw new Exception("sync option exceeds settings width");
   if(id.StartsWith("SyncRecentHours",StringComparison.Ordinal)&&AutomationProperties.GetAutomationId(button).StartsWith("SyncRecentHours",StringComparison.Ordinal)){var bounds=button.TransformToAncestor(scroll).TransformBounds(new Rect(0,0,button.ActualWidth,button.ActualHeight));if(bounds.Top<0||bounds.Bottom>scroll.ActualHeight+1)throw new Exception("time option is outside the scrolled viewport");}
  }
 }
 [STAThread]static int Main(string[] args){try{
  string output=Path.GetFullPath(args[0]);bool baseline=args.Length>1&&args[1]=="baseline";Directory.CreateDirectory(output);Settings.DataDir=Path.Combine(output,"data");
  new Settings{UiScale=1,DefaultUiScale=1,Width=1600,Height=700,ThemeMode="light",Topmost=false,TopmostMode="none",ReserveWorkArea=false}.Save();
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var w=new StripWindow(true);app.MainWindow=w;Exception failure=null;
  w.Loaded+=async delegate{try{
   await Task.Delay(250);w.ShowSettings();await Task.Delay(250);
   if(!baseline){
    Choice(w,"SyncRecentLimit-30").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Choice(w,"SyncRecentHours-6").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    // Demo windows intentionally suppress automatic writes. Persist the edited
    // config explicitly to verify serialization without starting desktop polling.
    w.Config.Save();
    var saved=Settings.Load();if((int)typeof(Settings).GetField("SyncRecentLimit").GetValue(saved)!=30||(int)typeof(Settings).GetField("SyncRecentHours").GetValue(saved)!=6)throw new Exception("sync choices did not save");
    Choice(w,"SyncRecentLimit-20").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Choice(w,"SyncRecentHours-24").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   }
   await Show(w,baseline);w.Snapshot(Path.Combine(output,"wide.png"));
   w.Width=480;w.Height=900;await Task.Delay(250);await Show(w,baseline);w.Snapshot(Path.Combine(output,"portrait.png"));
   w.Height=315;w.Config.UiScale=1.5;w.ApplyTheme();await Task.Delay(250);await Show(w,baseline);w.Snapshot(Path.Combine(output,"minimum.png"));
   if(!baseline){await Show(w,baseline,"SyncRecentHours-1");w.Snapshot(Path.Combine(output,"minimum-window.png"));}
   w.Height=900;w.SetTheme("dark");await Task.Delay(250);await Show(w,baseline);w.Snapshot(Path.Combine(output,"dark.png"));
  }catch(Exception error){failure=error;}finally{w.Close();app.Shutdown();}};
  w.Show();app.Run();if(failure!=null)throw failure;Console.WriteLine("PASS: sync settings save/reload, wrapping and four window/theme renders");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
