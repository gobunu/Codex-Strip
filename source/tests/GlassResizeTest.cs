using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexStrip;

class GlassResizeTest {
 static IEnumerable<Image> Images(DependencyObject parent){for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);var image=child as Image;if(image!=null)yield return image;foreach(var nested in Images(child))yield return nested;}}
 static void Tick(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
 [STAThread] static int Main(string[] args){try{
  string output=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"glass-resize-results");Directory.CreateDirectory(output);
  Settings.DataDir=Path.Combine(output,"data");Directory.CreateDirectory(Settings.DataDir);
  int width=1920,height=450;var pixels=new byte[width*height*4];
  for(int y=0;y<height;y++)for(int x=0;x<width;x++){int at=(y*width+x)*4;pixels[at]=(byte)(x*255/width);pixels[at+1]=(byte)(y*255/height);pixels[at+2]=(byte)(255-x*255/width);pixels[at+3]=(byte)((x/180)%3==0?0:(x/180)%3==1?128:255);}
  var wallpaper=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(wallpaper));string imagePath=Path.Combine(output,"transparent-wallpaper.png");using(var file=File.Create(imagePath))encoder.Save(file);
  new Settings{UiScale=1,DefaultUiScale=1,SleepWallpaperPath=imagePath,SleepGlassRefraction=true,ThemeMode="light",Width=600,Height=300,ReserveWorkArea=false,Topmost=false,TopmostMode="none"}.Save();
  var app=new Application();var window=new StripWindow(true);window.ShowInTaskbar=false;window.Show();window.ShowSleepDemo();Tick();window.UpdateLayout();Tick();
  var edge=Images(window).FirstOrDefault(i=>i.Source is BitmapSource&&((BitmapSource)i.Source).Format==PixelFormats.Pbgra32&&i.ActualWidth>100&&i.ActualWidth<260&&i.ActualHeight>100);
  if(edge==null)throw new Exception("Metric refraction edge not found");
  var before=edge.Source;double tileWidth=edge.ActualWidth;window.Snapshot(Path.Combine(output,"before-600.png"));
  window.Width=800;window.UpdateLayout();Tick();window.UpdateLayout();Tick();
  if(Math.Abs(edge.ActualWidth-tileWidth)>.1)throw new Exception("Fixture did not keep metric panel width constant");
  if(object.ReferenceEquals(before,edge.Source))throw new Exception("Background resize did not invalidate refraction cache");
  window.Snapshot(Path.Combine(output,"after-800.png"));var resized=edge.Source;
  DependencyObject ancestor=edge;while(ancestor!=null&&!(ancestor is ScrollViewer))ancestor=VisualTreeHelper.GetParent(ancestor);var scroll=ancestor as ScrollViewer;if(scroll==null)throw new Exception("Metric scroller not found");scroll.ScrollToHorizontalOffset(80);Tick();window.UpdateLayout();Tick();if(scroll.HorizontalOffset<1)throw new Exception("Fixture did not scroll metric panels");
  if(object.ReferenceEquals(resized,edge.Source))throw new Exception("Metric scrolling did not realign refraction");
  window.Snapshot(Path.Combine(output,"after-scroll.png"));window.Exiting=true;window.Close();
  Console.WriteLine("PASS: stable-width metric panels refresh refraction after background resize and scrolling; transparent PNG rendered.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
