using System;using System.IO;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows;using System.Windows.Interop;using CodexStrip;
class ForegroundTest {
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int c);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [STAThread]static void Main(){var app=new Application();var w=new Window{Title="Codex foreground verification",Width=400,Height=220};w.Loaded+=async delegate{IntPtr target=IntPtr.Zero;try{target=CodexForeground.Find();if(target==IntPtr.Zero)throw new Exception("Codex window not found");w.Activate();await Task.Delay(150);CodexForeground.Activate(target);await Task.Delay(250);if(CodexForeground.GetForegroundWindow()!=target)throw new Exception("background activation failed target="+target+" foreground="+CodexForeground.GetForegroundWindow());ShowWindow(target,6);await Task.Delay(200);if(!IsIconic(target))throw new Exception("minimize setup failed");w.Activate();await Task.Delay(100);CodexForeground.Activate(target);await Task.Delay(250);if(CodexForeground.GetForegroundWindow()!=target||IsIconic(target))throw new Exception("minimized restore failed");File.WriteAllText("work/foreground-test.txt","PASS: actual Codex desktop located; background activation; minimized window restored and foreground verified");}catch(Exception e){File.WriteAllText("work/foreground-test.txt",e.ToString());}finally{if(target!=IntPtr.Zero)CodexForeground.Activate(target);w.Close();}};app.Run(w);}
}


