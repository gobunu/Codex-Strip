using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexStrip;

class GlassPixelCacheTest {
 static byte[] Pixels(BitmapSource image){var bytes=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(bytes,image.PixelWidth*4,0);return bytes;}
 [STAThread] static int Main(){try{
  AppDomain.MonitoringIsEnabled=true;
  int width=3840,height=2160;var bytes=new byte[width*height*4];for(int at=0;at<bytes.Length;at+=4){bytes[at]=80;bytes[at+1]=140;bytes[at+2]=220;bytes[at+3]=255;}
  var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,bytes,width*4);image.Freeze();
  var scene=new Size(800,300);GlassBackdrop.Edge(image,scene,new Rect(0,40,220,220),16,.4,1,true,Colors.White);
  long before=AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
  for(int i=0;i<5;i++)GlassBackdrop.Edge(image,scene,new Rect(i*80,40,220,220),16,.4,1,true,Colors.White);
  long allocated=AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize-before;
  if(allocated>=8*1024*1024)throw new Exception("Scrolling copied full wallpaper pixels: "+allocated);
  Console.WriteLine("PASS: five 4K-wallpaper refraction updates allocated "+allocated+" bytes after one shared input cache.");
  var mutable=new WriteableBitmap(8,8,96,96,PixelFormats.Bgra32,null);var colors=new byte[8*8*4];for(int at=0;at<colors.Length;at+=4){colors[at+2]=255;colors[at+3]=255;}mutable.WritePixels(new Int32Rect(0,0,8,8),colors,8*4,0);
  var first=GlassBackdrop.Edge(mutable,new Size(8,8),new Rect(0,0,8,8),3,.4,1,true,Colors.White);
  for(int at=0;at<colors.Length;at+=4){colors[at]=255;colors[at+2]=0;}mutable.WritePixels(new Int32Rect(0,0,8,8),colors,8*4,0);
  var second=GlassBackdrop.Edge(mutable,new Size(8,8),new Rect(0,0,8,8),3,.4,1,true,Colors.White);
  if(Pixels(first).SequenceEqual(Pixels(second)))throw new Exception("Mutable wallpaper pixels were cached after an update");
  Console.WriteLine("PASS: mutable bitmap updates retain fresh pixels.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
