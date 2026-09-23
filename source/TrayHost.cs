using System;
using System.Windows;
using Forms=System.Windows.Forms;

namespace CodexStrip {
 public sealed class TrayHost:IDisposable {
  readonly StripWindow window;
  readonly Forms.NotifyIcon icon;
  readonly Forms.ContextMenuStrip menu;
  readonly System.Drawing.Icon artwork;
  bool disposed;
  public TrayHost(StripWindow w){window=w;artwork=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);menu=new Forms.ContextMenuStrip();
   menu.Items.Add("显示／隐藏看板",null,delegate{Toggle();});
   menu.Items.Add("账户额度",null,delegate{Show();window.ShowUsage();});
   menu.Items.Add("设置",null,delegate{Show();window.ShowSettings();});
   menu.Items.Add(new Forms.ToolStripSeparator());
   menu.Items.Add("退出 Codex Strip",null,delegate{Exit();});
   icon=new Forms.NotifyIcon{Icon=artwork,Text="Codex Strip · 点击显示／隐藏",ContextMenuStrip=menu,Visible=true};
   icon.MouseClick+=(s,e)=>{if(e.Button==Forms.MouseButtons.Left)Toggle();};
   window.TrayMode=true;window.StateChanged+=OnStateChanged;window.Closed+=OnClosed;
  }
  public bool Visible {get{return !disposed&&icon.Visible;}}
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int command);
  public void Show(){if(disposed)return;if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.Show();ShowWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle,9);window.Activate();if(window.Config.ReserveWorkArea)window.Dispatcher.BeginInvoke(new Action(()=>{if(window.IsVisible)window.ApplyDockSettings();}),System.Windows.Threading.DispatcherPriority.ContextIdle);}
  public void Toggle(){if(window.IsVisible)window.HideToTray();else Show();}
  public void Exit(){window.Exiting=true;window.Close();}
  void OnStateChanged(object s,EventArgs e){if(window.WindowState==WindowState.Minimized)window.HideToTray();}
  void OnClosed(object s,EventArgs e){Dispose();}
  public void Dispose(){if(disposed)return;disposed=true;window.StateChanged-=OnStateChanged;window.Closed-=OnClosed;icon.Visible=false;icon.Dispose();menu.Dispose();if(artwork!=null)artwork.Dispose();}
 }
}
