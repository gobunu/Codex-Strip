using System;using System.Runtime.InteropServices;using System.Windows;using System.Windows.Interop;using System.Windows.Threading;
namespace CodexStrip {
 public static class WindowGeometry {
  [StructLayout(LayoutKind.Sequential)]public struct RECT{public int left,top,right,bottom;}
  [StructLayout(LayoutKind.Sequential)]struct INFO{public int size;public RECT monitor,work;public uint flags;}
  [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
  [DllImport("user32.dll")]public static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")]static extern bool GetMonitorInfo(IntPtr monitor,ref INFO info);
  [DllImport("user32.dll")]public static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  public static RECT Bounds(IntPtr hwnd,bool work){var info=new INFO{size=Marshal.SizeOf(typeof(INFO))};GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info);return work?info.work:info.monitor;}
  static int Clamp(int value,int low,int high){return Math.Max(low,Math.Min(high,value));}
  public static RECT Fit(RECT source,RECT work,int width,int height,int minWidth,int minHeight,DockEdge? edge){int inset=16;int ww=work.right-work.left,wh=work.bottom-work.top;width=Math.Min(Math.Max(minWidth,width),Math.Max(1,ww-2*inset));height=Math.Min(Math.Max(minHeight,height),Math.Max(1,wh-2*inset));int x=(source.left+source.right-width)/2,y=(source.top+source.bottom-height)/2;if(edge==DockEdge.Left)x=work.left+inset;else if(edge==DockEdge.Right)x=work.right-width-inset;else if(edge==DockEdge.Top)y=work.top+inset;else if(edge==DockEdge.Bottom)y=work.bottom-height-inset;x=Clamp(x,work.left+inset,work.right-width-inset);y=Clamp(y,work.top+inset,work.bottom-height-inset);return new RECT{left=x,top=y,right=x+width,bottom=y+height};}
  public static void FloatNearby(Window window,DockEdge edge){var hwnd=new WindowInteropHelper(window).Handle;RECT source;GetWindowRect(hwnd,out source);double scale=Math.Max(1,GetDpiForWindow(hwnd))/96.0;var target=Fit(source,Bounds(hwnd,true),(int)((source.right-source.left)*.78),(int)((source.bottom-source.top)*.78),(int)Math.Ceiling(window.MinWidth*scale),(int)Math.Ceiling(window.MinHeight*scale),edge);SetWindowPos(hwnd,IntPtr.Zero,target.left,target.top,target.right-target.left,target.bottom-target.top,0x14);}
  // Work in physical pixels; DPI is used only for readable minimum sizes.
  public static string NearestEdge(RECT r,RECT screen,bool vertical){return vertical?(Math.Abs(r.left-screen.left)<=Math.Abs(screen.right-r.right)?"left":"right"):(Math.Abs(r.top-screen.top)<=Math.Abs(screen.bottom-r.bottom)?"top":"bottom");}
  public static double[] CapturePlacement(Window window){var h=new WindowInteropHelper(window).Handle;RECT r;GetWindowRect(h,out r);var monitor=Bounds(h,false);double scale=Math.Max(96,GetDpiForWindow(h))/96.0;return new[]{(r.left-monitor.left)/scale,(r.top-monitor.top)/scale,(r.right-r.left)/scale,(r.bottom-r.top)/scale};}
  public static void RestorePlacement(Window window,double[] p){var h=new WindowInteropHelper(window).Handle;var monitor=Bounds(h,false);var work=Bounds(h,true);double scale=Math.Max(96,GetDpiForWindow(h))/96.0;int width=Math.Min(work.right-work.left,(int)Math.Round(Math.Max(window.MinWidth,p[2])*scale));int height=Math.Min(work.bottom-work.top,(int)Math.Round(Math.Max(window.MinHeight,p[3])*scale));int x=Clamp(monitor.left+(int)Math.Round(p[0]*scale),work.left,work.right-width),y=Clamp(monitor.top+(int)Math.Round(p[1]*scale),work.top,work.bottom-height);SetWindowPos(h,IntPtr.Zero,x,y,width,height,0x14);}
  public static string MonitorKey(Window window){return System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).DeviceName;}
  
  public static RECT AdaptiveTranspose(RECT source,RECT work,int minWidth,int minHeight,double targetThickness=0){
   double sw=source.right-source.left,sh=source.bottom-source.top;bool wasVertical=sh>sw;
   double aw=Math.Max(1,work.right-work.left-32),ah=Math.Max(1,work.bottom-work.top-32);
   double coverage=Math.Min(1,wasVertical?sh/ah:sw/aw);
   double thickness=targetThickness>0?targetThickness:(wasVertical?minHeight:minWidth);
   double length=coverage*(wasVertical?aw:ah);
   // Keep the requested orientation while fitting the available display.
   if(wasVertical){thickness=Math.Max(minHeight,Math.Min(thickness,aw/1.35));length=Math.Max(length,Math.Max(minWidth,thickness*1.35));}
   else{thickness=Math.Max(minWidth,Math.Min(thickness,ah/1.35));length=Math.Max(length,Math.Max(minHeight,thickness*1.35));}
   return Fit(source,work,(int)Math.Round(wasVertical?length:thickness),(int)Math.Round(wasVertical?thickness:length),minWidth,minHeight,null);
  }
  public static void Transpose(Window window,double thickness=0){var hwnd=new WindowInteropHelper(window).Handle;RECT source;GetWindowRect(hwnd,out source);double scale=Math.Max(1,GetDpiForWindow(hwnd))/96.0;var target=AdaptiveTranspose(source,Bounds(hwnd,true),(int)Math.Ceiling(window.MinWidth*scale),(int)Math.Ceiling(window.MinHeight*scale),thickness*scale);SetWindowPos(hwnd,IntPtr.Zero,target.left,target.top,target.right-target.left,target.bottom-target.top,0x14);}
 }
 public static class CodexForeground {
  delegate bool EnumProc(IntPtr h,IntPtr p);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr p);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr h,uint command);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll")]static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int command);
  [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint pid);
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")]static extern bool AttachThreadInput(uint a,uint b,bool attach);
  public static IntPtr Find(){var ids=new System.Collections.Generic.HashSet<int>();foreach(var p in System.Diagnostics.Process.GetProcesses()){try{string path=p.MainModule.FileName.Replace('/','\\');if(path.IndexOf("\\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("\\OpenAI\\Codex\\",StringComparison.OrdinalIgnoreCase)>=0)ids.Add(p.Id);}catch{}finally{p.Dispose();}}IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{uint pid;GetWindowThreadProcessId(h,out pid);if(ids.Contains((int)pid)&&IsWindowVisible(h)&&GetWindow(h,4)==IntPtr.Zero&&GetWindowTextLength(h)>0){found=h;return false;}return true;},IntPtr.Zero);return found;}
  // Called synchronously from the user's click, before any asynchronous IPC work.
  public static bool Activate(IntPtr h){if(h==IntPtr.Zero)return false;uint pid;uint targetThread=GetWindowThreadProcessId(h,out pid);AllowSetForegroundWindow(pid);uint unused;uint foregroundThread=GetWindowThreadProcessId(GetForegroundWindow(),out unused),current=GetCurrentThreadId();bool a=foregroundThread!=0&&foregroundThread!=current&&AttachThreadInput(current,foregroundThread,true);bool b=targetThread!=current&&targetThread!=foregroundThread&&AttachThreadInput(current,targetThread,true);try{if(IsIconic(h))ShowWindow(h,9);SetForegroundWindow(h);}finally{if(b)AttachThreadInput(current,targetThread,false);if(a)AttachThreadInput(current,foregroundThread,false);}return GetForegroundWindow()==h;}
 }
 public sealed class TopmostPolicy:IDisposable {
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint id);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder name,int max);
  readonly Window window;readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};string mode="always";bool yielded;public bool FullscreenAhead{get;private set;}
  public TopmostPolicy(Window owner){window=owner;timer.Tick+=delegate{Refresh();};window.Closed+=Closed;timer.Start();}
  public void SetMode(string value){mode=value;Refresh();}
  public void Refresh(){var own=new WindowInteropHelper(window).Handle;if(own==IntPtr.Zero||!window.IsVisible)return;var strip=window as StripWindow;if(strip!=null&&strip.Fullscreen){window.Topmost=true;return;}var foreground=GetForegroundWindow();bool full=false;if(foreground!=IntPtr.Zero&&foreground!=own&&IsWindowVisible(foreground)&&WindowGeometry.MonitorFromWindow(foreground,2)==WindowGeometry.MonitorFromWindow(own,2)){uint otherId,ownId;GetWindowThreadProcessId(foreground,out otherId);GetWindowThreadProcessId(own,out ownId);var name=new System.Text.StringBuilder(128);GetClassName(foreground,name,128);if(otherId!=ownId&&name.ToString()!="Progman"&&name.ToString()!="WorkerW"&&name.ToString()!="Shell_TrayWnd"){WindowGeometry.RECT rect;WindowGeometry.GetWindowRect(foreground,out rect);var screen=WindowGeometry.Bounds(foreground,false);full=rect.left<=screen.left+2&&rect.top<=screen.top+2&&rect.right>=screen.right-2&&rect.bottom>=screen.bottom-2;}}
   FullscreenAhead=full;bool top=mode=="always"||mode=="window"&&!full;if(window.Topmost!=top)window.Topmost=top;if(mode=="window"&&full&&!yielded)WindowGeometry.SetWindowPos(own,new IntPtr(1),0,0,0,0,0x13);if(mode=="always"&&full)WindowGeometry.SetWindowPos(own,new IntPtr(-1),0,0,0,0,0x13);yielded=mode=="window"&&full;
  }
  void Closed(object sender,EventArgs e){Dispose();}
  public void Dispose(){timer.Stop();window.Closed-=Closed;}
 }
}
