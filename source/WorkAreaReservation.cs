using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
namespace CodexStrip {
 public enum DockEdge { Top, Bottom, Left, Right }
 public sealed class WorkAreaReservation:IDisposable {
  [StructLayout(LayoutKind.Sequential)]public struct RECT {public int left,top,right,bottom;}
  [StructLayout(LayoutKind.Sequential)]struct DATA {public int cbSize;public IntPtr hWnd;public uint callback;public uint edge;public RECT rect;public IntPtr param;}
  [StructLayout(LayoutKind.Sequential)]struct MONITORINFO {public int size;public RECT monitor,work;public uint flags;}
  [DllImport("shell32.dll")]static extern UIntPtr SHAppBarMessage(uint message,ref DATA data);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
  [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")]static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
  [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
  readonly Window window;IntPtr hwnd,monitor;HwndSource source;uint callback,restart;bool active,positioning,queued,disposed,captured;DockEdge edge;double thickness;string display="";RECT last;bool positioned;Rect floating;ResizeMode floatingResize;
  public bool Active {get{return active;}}
  public RECT Reserved {get{return last;}}
  public WorkAreaReservation(Window owner){window=owner;}
  uint EdgeCode {get{return edge==DockEdge.Left?0u:edge==DockEdge.Top?1u:edge==DockEdge.Right?2u:3u;}}
  DATA Data(){return new DATA{cbSize=Marshal.SizeOf(typeof(DATA)),hWnd=hwnd,callback=callback,edge=EdgeCode};}
  public void Configure(DockEdge side,double size,string screen){edge=side;thickness=size;display=screen??"";positioned=false;if(hwnd!=IntPtr.Zero)SelectMonitor();if(window.IsVisible)Register();}
  void Initialize(){if(hwnd!=IntPtr.Zero)return;hwnd=new WindowInteropHelper(window).EnsureHandle();source=HwndSource.FromHwnd(hwnd);callback=RegisterWindowMessage("CodexStrip.AppBar.Callback");restart=RegisterWindowMessage("TaskbarCreated");source.AddHook(Hook);window.IsVisibleChanged+=VisibilityChanged;window.Closed+=Closed;}
  void SelectMonitor(){monitor=MonitorFromWindow(hwnd,2);if(display.Length>0)foreach(var screen in System.Windows.Forms.Screen.AllScreens)if(screen.DeviceName==display){var rect=screen.Bounds;var point=new POINT{X=rect.Left+rect.Width/2,Y=rect.Top+rect.Height/2};monitor=MonitorFromPoint(point,2);break;}}
  [StructLayout(LayoutKind.Sequential)]struct POINT {public int X,Y;}
  [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(POINT point,uint flags);
  public void Register(){if(disposed)return;Initialize();if(!active){if(!captured){floating=new Rect(window.Left,window.Top,window.Width,window.Height);floatingResize=window.ResizeMode;captured=true;}SelectMonitor();var d=Data();if(SHAppBarMessage(0,ref d)==UIntPtr.Zero)throw new InvalidOperationException("Windows rejected AppBar registration");active=true;window.ResizeMode=ResizeMode.NoResize;}SetPosition();}
  public void SetPosition(){if(!active||positioning||disposed||IsIconic(hwnd))return;positioning=true;try{var info=new MONITORINFO{size=Marshal.SizeOf(typeof(MONITORINFO))};if(!GetMonitorInfo(monitor,ref info)){SelectMonitor();if(!GetMonitorInfo(monitor,ref info))return;}uint dpi=GetDpiForWindow(hwnd);if(dpi==0)dpi=96;bool horizontal=edge==DockEdge.Top||edge==DockEdge.Bottom;int full=horizontal?info.monitor.bottom-info.monitor.top:info.monitor.right-info.monitor.left;int minimum=(int)Math.Ceiling((horizontal?window.MinHeight:window.MinWidth)*dpi/96.0);int size=Math.Min((int)(full*.9),Math.Max(minimum,(int)Math.Round(thickness*dpi/96.0)));var d=Data();d.rect=info.monitor;
   if(edge==DockEdge.Top)d.rect.bottom=d.rect.top+size;else if(edge==DockEdge.Bottom)d.rect.top=d.rect.bottom-size;else if(edge==DockEdge.Left)d.rect.right=d.rect.left+size;else d.rect.left=d.rect.right-size;
   SHAppBarMessage(2,ref d);if(edge==DockEdge.Top)d.rect.bottom=d.rect.top+size;else if(edge==DockEdge.Bottom)d.rect.top=d.rect.bottom-size;else if(edge==DockEdge.Left)d.rect.right=d.rect.left+size;else d.rect.left=d.rect.right-size;
   RECT actual;GetWindowRect(hwnd,out actual);if(positioned&&Same(last,d.rect)){if(!Same(actual,last))SetWindowPos(hwnd,IntPtr.Zero,last.left,last.top,last.right-last.left,last.bottom-last.top,0x14);return;}SHAppBarMessage(3,ref d);last=d.rect;positioned=true;SetWindowPos(hwnd,IntPtr.Zero,d.rect.left,d.rect.top,d.rect.right-d.rect.left,d.rect.bottom-d.rect.top,0x14);
  }finally{positioning=false;}}
  static bool Same(RECT a,RECT b){return a.left==b.left&&a.top==b.top&&a.right==b.right&&a.bottom==b.bottom;}
  void QueuePosition(){if(queued||disposed)return;queued=true;window.Dispatcher.BeginInvoke(new Action(()=>{queued=false;if(active)SetPosition();}),DispatcherPriority.Background);}
  IntPtr Hook(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if((uint)msg==callback&&w.ToInt32()==1)QueuePosition();else if((uint)msg==restart&&active){active=false;positioned=false;Register();}else if(msg==0x7E||msg==0x2E0){positioned=false;QueuePosition();}else if(active&&msg==0x47&&!positioning){var d=Data();SHAppBarMessage(9,ref d);RECT actual;GetWindowRect(hwnd,out actual);if(positioned&&!IsIconic(hwnd)&&!Same(actual,last))QueuePosition();}else if(active&&msg==6){var d=Data();d.param=new IntPtr((w.ToInt64()&0xffff)!=0?1:0);SHAppBarMessage(6,ref d);}return IntPtr.Zero;}
  void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e){if((bool)e.NewValue){Register();QueuePosition();}else Unregister(false);}
  void Closed(object sender,EventArgs e){Dispose();}
  public void Unregister(bool restore){if(active){var d=Data();SHAppBarMessage(1,ref d);active=false;positioned=false;}if(restore||disposed)window.ResizeMode=floatingResize;if(restore&&floating.Width>0)WindowGeometry.FloatNearby(window,edge);}
  public void Dispose(){if(disposed)return;Unregister(false);disposed=true;window.IsVisibleChanged-=VisibilityChanged;window.Closed-=Closed;if(source!=null&&!source.IsDisposed)source.RemoveHook(Hook);}
 }
}
