using System;using System.Threading.Tasks;using System.Windows;using CodexStrip;
class IconPreview {
 [STAThread]static void Main(){var app=new Application();var w=new StripWindow(true){Width=320,Height=900};w.Loaded+=async delegate{try{await Task.Delay(200);w.SetTheme("light");w.Snapshot("work/transpose-icon-light.png");w.SetTheme("dark");w.Snapshot("work/transpose-icon-dark.png");}finally{w.Close();}};app.Run(w);}
}
