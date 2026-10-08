using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexStrip {
 // Rounded-rectangle edge refraction adapted from Kyant's Backdrop shader approach.
 // Copyright 2025 Kyant. Apache-2.0; see THIRD_PARTY_NOTICES.txt.
 // This WPF adaptation caches a static wallpaper edge, rather than running a
 // full-screen shader on every resource-monitor animation frame.
 public static class GlassBackdrop {
  sealed class PixelBuffer {
   public readonly byte[] Bytes;
   public PixelBuffer(BitmapSource image){var source=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);int stride=image.PixelWidth*4;Bytes=new byte[stride*image.PixelHeight];source.CopyPixels(Bytes,stride,0);}
  }
  static readonly ConditionalWeakTable<BitmapSource,PixelBuffer> pixelCache=new ConditionalWeakTable<BitmapSource,PixelBuffer>();
  public static BitmapSource Edge(BitmapSource image,Size scene,Rect panel,double radius,double dim,double density,bool refraction=false,Color? backgroundColor=null){
   density=Math.Max(1,Math.Min(2,density));int width=Math.Max(1,(int)Math.Ceiling(panel.Width*density)),height=Math.Max(1,(int)Math.Ceiling(panel.Height*density));
   int sw=image.PixelWidth,sh=image.PixelHeight,stride=sw*4;byte[] input=null;if(refraction)input=(image.IsFrozen?pixelCache.GetValue(image,p=>new PixelBuffer(p)):new PixelBuffer(image)).Bytes;var output=new byte[width*height*4];
   double scale=Math.Max(scene.Width/sw,scene.Height/sh),offsetX=(scene.Width-sw*scale)/2,offsetY=(scene.Height-sh*scale)/2;
   double halfW=panel.Width/2,halfH=panel.Height/2,r=Math.Min(radius,Math.Min(halfW,halfH)),band=Math.Min(refraction?18:6,Math.Min(r,Math.Min(halfW,halfH)*.65)),amount=refraction?Math.Min(32,Math.Min(halfW,halfH)*1.5):0,gradientRadius=Math.Min(r*1.5,Math.Min(halfW,halfH));
   dim=Math.Max(.2,Math.Min(.8,dim));Color background=backgroundColor??Colors.White;double[] baseChannels={background.B,background.G,background.R};
   for(int y=0;y<height;y++)for(int x=0;x<width;x++){
    double px=(x+.5)/density,py=(y+.5)/density,cx=px-halfW,cy=py-halfH;
    if(Math.Min(Math.Min(px,panel.Width-px),Math.Min(py,panel.Height-py))>r+band)continue;
    double qx=Math.Abs(cx)-halfW+r,qy=Math.Abs(cy)-halfH+r,mx=Math.Max(0,qx),my=Math.Max(0,qy);
    double distance=Math.Sqrt(mx*mx+my*my)+Math.Min(Math.Max(qx,qy),0)-r;
    if(distance>0||-distance>=band)continue;
    double t=1+distance/band,bend=amount*(1-Math.Sqrt(Math.Max(0,1-t*t)));
    double gx=Math.Max(0,Math.Abs(cx)-halfW+gradientRadius),gy=Math.Max(0,Math.Abs(cy)-halfH+gradientRadius),length=Math.Sqrt(gx*gx+gy*gy);
    if(length>0){gx=gx/length*Math.Sign(cx);gy=gy/length*Math.Sign(cy);}else if(Math.Abs(cx)-halfW>Math.Abs(cy)-halfH){gx=Math.Sign(cx);gy=0;}else{gx=0;gy=Math.Sign(cy);}
    double blend=Math.Min(1,t*5),alpha=blend*blend*(3-2*blend),shine=.36*Math.Pow(Math.Abs((gx+gy)*.70710678),3)*Math.Pow(t,5);
    // Clear glass only reflects light. It never resamples or moves wallpaper pixels.
    if(!refraction){int pixel=(y*width+x)*4;byte light=(byte)Math.Round(255*alpha*shine);output[pixel]=output[pixel+1]=output[pixel+2]=output[pixel+3]=light;continue;}
    double sx=(panel.X+px-gx*bend-offsetX)/scale,sy=(panel.Y+py-gy*bend-offsetY)/scale;
    sx=Math.Max(0,Math.Min(sw-1,sx));sy=Math.Max(0,Math.Min(sh-1,sy));int ix=(int)sx,iy=(int)sy;double fx=sx-ix,fy=sy-iy;
    int right=Math.Min(sw-1,ix+1),bottom=Math.Min(sh-1,iy+1),a=iy*stride+ix*4,b=iy*stride+right*4,c=bottom*stride+ix*4,d=bottom*stride+right*4,at=(y*width+x)*4;
    // Preserve the lens displacement through most of the band; blend only its
    // inner seam, where displacement already approaches zero.
    double opacity=((input[a+3]*(1-fx)+input[b+3]*fx)*(1-fy)+(input[c+3]*(1-fx)+input[d+3]*fx)*fy)/255;
    for(int channel=0;channel<3;channel++){
     // Interpolate premultiplied samples, then composite over the scene's canvas.
     double value=((input[a+channel]*input[a+3]*(1-fx)+input[b+channel]*input[b+3]*fx)*(1-fy)+(input[c+channel]*input[c+3]*(1-fx)+input[d+channel]*input[d+3]*fx)*fy)/255;
     value+=baseChannels[channel]*(1-opacity);
     double shade=channel==0?20:channel==1?10:7;value=value*(1-dim)+shade*dim;value=value*(1-shine)+255*shine;output[at+channel]=(byte)Math.Round(value*alpha);
    }
    output[at+3]=(byte)Math.Round(255*alpha);
   }
   var result=BitmapSource.Create(width,height,96*density,96*density,PixelFormats.Pbgra32,null,output,width*4);result.Freeze();return result;
  }
 }
}
