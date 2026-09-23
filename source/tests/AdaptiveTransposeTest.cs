using System;using System.IO;using System.Text;using CodexStrip;
class AdaptiveTransposeTest {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 [STAThread]static void Main(){var log=new StringBuilder();try{foreach(var size in new[]{new[]{1920,1080},new[]{1080,1920},new[]{3440,1440},new[]{1440,3440},new[]{1920,480},new[]{480,1920}})foreach(double dpi in new[]{1.0,1.25,1.5}){
 var work=new WindowGeometry.RECT{left=-size[0],top=40,right=0,bottom=40+size[1]};int mw=(int)(320*dpi),mh=(int)(210*dpi);var src=WindowGeometry.Fit(work,work,(int)((size[0]-32)*.8),(int)(240*dpi),mw,mh,null);
 var v=WindowGeometry.AdaptiveTranspose(src,work,mw,mh);Check(v.left>=work.left&&v.right<=work.right&&v.top>=work.top&&v.bottom<=work.bottom,"vertical outside display");if(size[1]-32>mw*1.35)Check(v.bottom-v.top>v.right-v.left,"vertical orientation");var h=WindowGeometry.AdaptiveTranspose(v,work,mw,mh);Check(h.left>=work.left&&h.right<=work.right&&h.top>=work.top&&h.bottom<=work.bottom,"horizontal outside display");if(size[0]-32>mh*1.35)Check(h.right-h.left>h.bottom-h.top,"horizontal orientation");
 if(size[0]==1920&&size[1]==1080&&dpi==1){Check(v.right-v.left==320,"first vertical width must be 320");Check(Math.Abs((v.bottom-v.top)/(double)(size[1]-32)-.8)<.01,"coverage changed");var repeat=h;for(int i=0;i<20;i++)repeat=WindowGeometry.AdaptiveTranspose(repeat,work,mw,mh);Check(Math.Abs((repeat.right-repeat.left)-(h.right-h.left))<5,"repeated transpose drift");}
 log.AppendLine(size[0]+"x"+size[1]+" @"+dpi+": "+(src.right-src.left)+"x"+(src.bottom-src.top)+" -> "+(v.right-v.left)+"x"+(v.bottom-v.top)+" -> "+(h.right-h.left)+"x"+(h.bottom-h.top));
 }log.AppendLine("PASS: screen coverage, orientation, bounds, DPI, negative monitor origin and repeated transpose");}catch(Exception e){log.AppendLine("FAIL: "+e);}File.WriteAllText("work/adaptive-transpose-test.txt",log.ToString());}
}
