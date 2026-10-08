using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexStrip;

class SleepFadeTest {
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static readonly List<string> readings=new List<string>();
 static T Field<T>(StripWindow window,string name){return (T)typeof(StripWindow).GetField(name,Private).GetValue(window);}
 static Grid Layer(StripWindow window){return Field<Grid>(window,"sleepLayer");}
 static Border Tasks(StripWindow window){return Field<Border>(window,"taskPage");}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Frame(StripWindow window,string output,string name){readings.Add(name+" opacity="+Layer(window).Opacity.ToString("0.000")+" sleeping="+window.Sleeping);Design.Capture(window,Path.Combine(output,name+".png"));}
 static void Restored(StripWindow window){Check(!window.Sleeping&&Layer(window).Visibility==Visibility.Collapsed&&Layer(window).Opacity==0&&!Layer(window).IsHitTestVisible&&Tasks(window).Visibility==Visibility.Visible&&Tasks(window).IsHitTestVisible,"task page was not fully restored");}
 static void Asleep(StripWindow window){Check(window.Sleeping&&Layer(window).Visibility==Visibility.Visible&&Layer(window).Opacity==1&&Layer(window).IsHitTestVisible&&Tasks(window).Visibility==Visibility.Collapsed&&!Tasks(window).IsHitTestVisible,"resource page did not finish fading in");}
 static bool Within(DependencyObject target,DependencyObject parent){while(target!=null){if(target==parent)return true;target=VisualTreeHelper.GetParent(target);}return false;}

 static async Task Frames(StripWindow window,string output,string name){
  window.WakeSleep();await Task.Delay(220);Restored(window);Frame(window,output,name+"-task");
  window.ShowSleepDemo();Check(Layer(window).IsHitTestVisible&&!Tasks(window).IsHitTestVisible,"enter input ownership");
  if(SystemParameters.ClientAreaAnimation)Check(Layer(window).Opacity<.01,"entry flashed its final frame before the first animation tick");
  Frame(window,output,name+"-enter-000");
  await Task.Delay(130);double first=Layer(window).Opacity;
  if(SystemParameters.ClientAreaAnimation)Check(first>0&&first<.9,"entry was abrupt instead of slow");
  Frame(window,output,name+"-enter-early");
  await Task.Delay(230);double middle=Layer(window).Opacity;
  if(SystemParameters.ClientAreaAnimation)Check(middle>=first&&middle<1,"entry did not pass through intermediate opacity");
  Frame(window,output,name+"-enter-middle");
  await Task.Delay(470);Asleep(window);Frame(window,output,name+"-sleep");
  window.WakeSleep();Check(!Layer(window).IsHitTestVisible&&Tasks(window).IsHitTestVisible&&Tasks(window).Visibility==Visibility.Visible,"exit delayed task interaction");
  if(SystemParameters.ClientAreaAnimation)Check(Layer(window).Opacity>.99,"exit flashed its final frame before the first animation tick");
  Check(Within(window.InputHitTest(new Point(120,24)) as DependencyObject,Tasks(window)),"fading resource overlay swallowed task input");
  Frame(window,output,name+"-exit-000");await Task.Delay(30);Frame(window,output,name+"-exit-early");
  await Task.Delay(160);Restored(window);Frame(window,output,name+"-restored");
 }

 static string Wallpaper(string output){
  int width=1280,height=400;var pixels=new byte[width*height*4];
  for(int y=0;y<height;y++)for(int x=0;x<width;x++){int at=(y*width+x)*4;pixels[at]=(byte)(70+x*90/width);pixels[at+1]=(byte)(25+y*50/height);pixels[at+2]=(byte)(35+x*70/width);pixels[at+3]=255;}
  var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
  string path=Path.Combine(output,"test-wallpaper.png");using(var file=File.Create(path))png.Save(file);return path;
 }

 [STAThread] static int Main(string[] args){
  string output=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sleep-fade-results");Directory.CreateDirectory(output);
  Settings.DataDir=Path.Combine(output,"data");new Settings{UiScale=1,DefaultUiScale=1,Width=1280,Height=400,ThemeMode="light",Topmost=false,TopmostMode="none",ReserveWorkArea=false}.Save();
  var app=new Application();var window=new StripWindow(true){ShowInTaskbar=false,ShowActivated=false,Left=SystemParameters.VirtualScreenLeft-2000,Top=SystemParameters.VirtualScreenTop-2000};int result=1;
  window.Loaded+=async delegate{try{
   await Task.Delay(160);await Frames(window,output,"light");
   window.Config.SleepWallpaperPath=Wallpaper(output);window.SetTheme("dark");await Task.Delay(100);await Frames(window,output,"wallpaper");
   window.ShowSleepDemo();await Task.Delay(120);double before=Layer(window).Opacity;
   Check(window.TestSleepClick(),"click during entry did not restore task input");
   if(SystemParameters.ClientAreaAnimation)Check(Math.Abs(Layer(window).Opacity-before)<.03,"interrupting entry jumped opacity");
   await Task.Delay(850);Restored(window);
   window.ShowSleepDemo();await Task.Delay(120);window.WakeSleep();await Task.Delay(35);before=Layer(window).Opacity;window.ShowSleepDemo();
   if(SystemParameters.ClientAreaAnimation)Check(Math.Abs(Layer(window).Opacity-before)<.03,"reversing exit jumped opacity");
   await Task.Delay(850);Asleep(window);window.WakeSleep();await Task.Delay(200);Restored(window);
   window.ShowSleepDemo();await Task.Delay(90);window.SetTheme("light");Asleep(window);await Task.Delay(800);Asleep(window);
   window.WakeSleep();window.SetTheme("dark");await Task.Delay(200);Restored(window);
   window.ShowSleepDemo();await Task.Delay(90);window.Width=1100;window.Height=340;await Task.Delay(800);Asleep(window);window.WakeSleep();await Task.Delay(200);Restored(window);
   File.WriteAllLines(Path.Combine(output,"frames.txt"),readings);
   Console.WriteLine("PASS: slow fade in; quick fade out; immediate task hit testing; interrupted entry and exit; stale callbacks; theme rebuild; resize; animations="+SystemParameters.ClientAreaAnimation);result=0;
  }catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());}finally{window.Close();}};
  app.Run(window);return result;
 }
}
