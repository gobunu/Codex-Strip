using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CodexStrip {
 public static class SleepPolicy {
  public static bool Quiet(bool connected,IEnumerable<Card> cards){if(!connected)return false;foreach(var card in cards){string state=card.Status??"";if(card.Stale||card.QuestionPending||card.Unread||!new[]{"completed","idle","notLoaded","interrupted"}.Contains(state))return false;}return true;}
  public static bool Due(DateTime since,DateTime now,int minutes){return since!=DateTime.MinValue&&(now-since).TotalMinutes>=Math.Max(1,minutes);}
 }

 public partial class StripWindow {
  Grid sleepLayer;UniformGrid sleepTiles;TextBlock sleepClock;
  readonly DispatcherTimer sleepTicker=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
  readonly ResourceSampler sampler=new ResourceSampler();ResourceSnapshot resources=new ResourceSnapshot();
  DateTime quietSince=DateTime.MinValue;bool sleeping,sampling;
  public bool Sleeping {get{return sleeping;}}
  public void StartSleepWatcher(){sleepTicker.Tick+=SleepTick;sleepTicker.Start();EvaluateSleep();}
  public void StopSleepWatcher(){sleepTicker.Stop();sleepTicker.Tick-=SleepTick;sampler.Dispose();}
  async void SleepTick(object sender,EventArgs e){EvaluateSleep();if(!sleeping||sampling||!IsVisible)return;sampling=true;try{ResourceSnapshot next=DemoMode?DemoResources():await Task.Run(()=>sampler.Sample(Config.SleepAdapterId));if(sleeping&&!closing){resources=next;UpdateSleepView();}}catch{}finally{sampling=false;}}
  static ResourceSnapshot DemoResources(){double t=DateTime.Now.Second;return new ResourceSnapshot{Cpu=28+t%9,Gpu=17+t%7,Memory=63.4,Download=4.8*1024*1024,Upload=720*1024,DiskRead=2.1*1024*1024,DiskWrite=820*1024,NetworkName="演示网卡"};}
  public void ShowSleepDemo(){resources=DemoResources();SetSleeping(true);}
  public bool TestSleepClick(){if(sleepLayer==null)return false;var click=new System.Windows.Input.MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent};sleepLayer.RaiseEvent(click);return !sleeping;}
  public void EvaluateSleep(){if(DemoMode)return;if(!Config.SleepAuto){if(sleeping)WakeSleep();quietSince=DateTime.MinValue;return;}if(PanelOpen){quietSince=DateTime.UtcNow;return;}bool quiet=SleepPolicy.Quiet(Engine.Connected,Engine.Cards.Values);if(!quiet){quietSince=DateTime.MinValue;if(sleeping)WakeSleep();return;}if(quietSince==DateTime.MinValue)quietSince=DateTime.UtcNow;if(!sleeping&&SleepPolicy.Due(quietSince,DateTime.UtcNow,Config.SleepIdleMinutes))SetSleeping(true);}
  public void WakeSleep(){if(sleeping)SetSleeping(false);quietSince=DateTime.UtcNow;}
  void SetSleeping(bool value){if(sleeping==value)return;sleeping=value;if(bodyGrid!=null)bodyGrid.Visibility=value?Visibility.Collapsed:Visibility.Visible;if(sleepLayer!=null)sleepLayer.Visibility=value?Visibility.Visible:Visibility.Collapsed;if(value)UpdateSleepView();}
  public void RefreshSleepAppearance(){if(sleepLayer==null)return;BuildSleepBackground();UpdateSleepView();}
  public void BuildSleepLayer(Grid layout){sleepLayer=new Grid{Visibility=sleeping?Visibility.Visible:Visibility.Collapsed,ClipToBounds=true,Cursor=Cursors.Hand};Grid.SetRow(sleepLayer,1);layout.Children.Add(sleepLayer);sleepLayer.PreviewMouseLeftButtonDown+=(s,e)=>{WakeSleep();e.Handled=true;};BuildSleepBackground();UpdateSleepView();if(bodyGrid!=null)bodyGrid.Visibility=sleeping?Visibility.Collapsed:Visibility.Visible;}
  void BuildSleepBackground(){sleepLayer.Children.Clear();Brush wallpaper=null;try{if(!string.IsNullOrWhiteSpace(Config.SleepWallpaperPath)&&File.Exists(Config.SleepWallpaperPath)){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(Path.GetFullPath(Config.SleepWallpaperPath));bitmap.EndInit();bitmap.Freeze();wallpaper=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Center};}}catch{}sleepLayer.Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#18243B"),(Color)ColorConverter.ConvertFromString("#13151F"),new Point(0,0),new Point(1,1));if(wallpaper!=null)sleepLayer.Children.Add(new Border{Background=wallpaper});
   byte alpha=(byte)Math.Round(255*Math.Max(.15,Math.Min(.85,Config.SleepDim)));sleepLayer.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(alpha,4,8,18))});
   var content=new Grid{Margin=new Thickness(12,6,12,8)};content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(30)});content.RowDefinitions.Add(new RowDefinition());sleepLayer.Children.Add(content);
   var header=new DockPanel();var prompt=Design.Text("静息 · 点击返回任务",12,"#FFFFFF");prompt.Opacity=.88;header.Children.Add(prompt);sleepClock=Design.Text("",12,"#FFFFFF");sleepClock.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(sleepClock,Dock.Right);header.Children.Add(sleepClock);content.Children.Add(header);
   sleepTiles=new UniformGrid{Columns=1,Rows=1};var scroll=new ScrollViewer{Content=sleepTiles,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Brushes.Transparent};Grid.SetRow(scroll,1);content.Children.Add(scroll);
  }
  Brush Accent(){string color=Config.SleepAccent=="green"?"#7DE0B0":Config.SleepAccent=="orange"?"#FFCB7D":Config.SleepAccent=="purple"?"#BBB0FF":"#7FCBFF";return Design.B(color);}
  string Speed(double? bytes){if(!bytes.HasValue)return "—";double value=Math.Max(0,bytes.Value);if(Config.SleepSpeedUnit=="mb")return (value/1048576).ToString("0.0")+" MB/s";if(Config.SleepSpeedUnit=="mbps")return (value*8/1000000).ToString("0.0")+" Mb/s";if(value>=1048576)return (value/1048576).ToString("0.0")+" MB/s";if(value>=1024)return (value/1024).ToString("0.0")+" KB/s";return value.ToString("0")+" B/s";}
  string Percent(double? value){return value.HasValue?value.Value.ToString("0")+"%":"—";}
  void AddMetric(string id){string label=id=="cpu"?"CPU":id=="gpu"?"GPU":id=="memory"?"内存":id=="network"?"网速":"磁盘";var frame=new Border{Margin=new Thickness(5),Padding=new Thickness(14,10,14,10),CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(Color.FromArgb(94,9,16,31)),BorderBrush=new SolidColorBrush(Color.FromArgb(76,255,255,255)),BorderThickness=new Thickness(1)};var content=new Grid();content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});frame.Child=content;var title=Design.Text(label,13,"#FFFFFF");title.Opacity=.78;content.Children.Add(title);
   var values=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(values,1);content.Children.Add(values);var accent=Accent();
   if(id=="cpu"||id=="gpu"||id=="memory"){double? amount=id=="cpu"?resources.Cpu:id=="gpu"?resources.Gpu:resources.Memory;var number=Design.Text(Percent(amount),32,"#FFFFFF");number.FontWeight=FontWeights.SemiBold;values.Children.Add(number);var rail=new Border{Height=4,CornerRadius=new CornerRadius(2),Background=new SolidColorBrush(Color.FromArgb(70,255,255,255)),Margin=new Thickness(0,8,0,0)};var fill=new Border{Height=4,CornerRadius=new CornerRadius(2),Background=accent,HorizontalAlignment=HorizontalAlignment.Left,Width=Math.Max(2,(amount??0)*1.45)};rail.Child=fill;rail.SizeChanged+=(s,e)=>fill.Width=Math.Max(0,rail.ActualWidth*(amount??0)/100);values.Children.Add(rail);}else{string first=id=="network"?"↓ "+Speed(resources.Download):"读 "+Speed(resources.DiskRead);string second=id=="network"?"↑ "+Speed(resources.Upload):"写 "+Speed(resources.DiskWrite);var a=Design.Text(first,21,"#FFFFFF");a.FontWeight=FontWeights.SemiBold;values.Children.Add(a);var b=Design.Text(second,17,"#DCE4F4");b.Margin=new Thickness(0,8,0,0);values.Children.Add(b);}
   sleepTiles.Children.Add(frame);
  }
  void UpdateSleepView(){if(sleepTiles==null)return;if(sleepClock!=null)sleepClock.Text=DateTime.Now.ToString("HH:mm:ss");sleepTiles.Children.Clear();var ids=Config.SleepWidgets.Where(Settings.SleepWidgetIds.Contains).Distinct().ToArray();if(ids.Length==0)ids=new[]{"cpu"};int columns=Portrait?Math.Min(2,ids.Length):Math.Min(5,ids.Length);sleepTiles.Columns=Math.Max(1,columns);sleepTiles.Rows=(int)Math.Ceiling(ids.Length/(double)columns);foreach(string id in ids)AddMetric(id);}
 }
}
