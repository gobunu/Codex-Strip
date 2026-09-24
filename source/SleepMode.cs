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
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CodexStrip {
 public static class SleepPolicy {
  public static bool Quiet(bool connected,IEnumerable<Card> cards){if(!connected)return false;foreach(var card in cards){string state=card.Status??"";if(card.Stale||card.QuestionPending||card.UnreadResult||!new[]{"completed","idle","notLoaded","interrupted"}.Contains(state))return false;}return true;}
  public static bool Due(DateTime since,DateTime now,int minutes){return since!=DateTime.MinValue&&(now-since).TotalMinutes>=Math.Max(1,minutes);}
 }

 // A damped second-order response keeps the current speed when a new sample arrives.
 public sealed class RingMotion {
  public double Value,Velocity,Target;
  public bool Step(double seconds){
   double dt=Math.Max(0,Math.Min(.05,seconds));
   double acceleration=Math.Max(-600,Math.Min(600,36*(Target-Value)-10*Velocity));
   Velocity=Math.Max(-160,Math.Min(160,Velocity+acceleration*dt));
   double next=Value+Velocity*dt;
   if((Target-Value)*(Target-next)<0){Value=Target;Velocity=0;}
   else Value=Math.Max(0,Math.Min(100,next));
   if(Math.Abs(Target-Value)<.03&&Math.Abs(Velocity)<.2){Value=Target;Velocity=0;}
   return Velocity!=0||Math.Abs(Target-Value)>=.03;
  }
 }

 public sealed class RingMetricMotion {
  public readonly RingMotion Total=new RingMotion();
  public readonly Dictionary<string,RingMotion> Parts=new Dictionary<string,RingMotion>();
  public readonly List<string> Order=new List<string>();
  public bool Available;
  public void SetTarget(double? total,ResourcePart[] parts){
   Available=total.HasValue;Total.Target=Math.Max(0,Math.Min(100,total??0));
   foreach(var motion in Parts.Values)motion.Target=0;
   foreach(var part in parts??new ResourcePart[0]){if(string.IsNullOrEmpty(part.Name))continue;RingMotion motion;if(!Parts.TryGetValue(part.Name,out motion)){motion=new RingMotion();Parts[part.Name]=motion;Order.Add(part.Name);}motion.Target=Math.Max(0,Math.Min(100,part.Percent));}
  }
  public void Snap(){Total.Value=Total.Target;Total.Velocity=0;foreach(string name in Order.ToArray()){var motion=Parts[name];motion.Value=motion.Target;motion.Velocity=0;if(motion.Target==0){Parts.Remove(name);Order.Remove(name);}}}
  public bool Step(double seconds){bool moving=Total.Step(seconds);foreach(string name in Order.ToArray()){var motion=Parts[name];moving=motion.Step(seconds)||moving;if(motion.Target==0&&motion.Value==0){Parts.Remove(name);Order.Remove(name);}}return moving;}
 }

 public partial class StripWindow {
  sealed class RingVisual {public Canvas Canvas;public TextBlock Label;public double Diameter;}
  Grid sleepLayer;Border taskPage;UniformGrid sleepTiles;TextBlock sleepClock;
  readonly DispatcherTimer sleepTicker=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
  readonly DispatcherTimer ringTicker=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
  readonly Dictionary<string,RingMetricMotion> ringMotions=new Dictionary<string,RingMetricMotion>();
  readonly Dictionary<string,RingVisual> ringVisuals=new Dictionary<string,RingVisual>();
  DateTime lastRingFrame=DateTime.MinValue;
  readonly ResourceSampler sampler=new ResourceSampler();ResourceSnapshot resources=new ResourceSnapshot();
  DateTime quietSince=DateTime.MinValue;bool sleeping,sampling,sleepWallpaperActive,sleepPortraitLayout;
  public bool Sleeping {get{return sleeping;}}
  public void StartSleepWatcher(){sleepTicker.Tick+=SleepTick;sleepTicker.Start();ringTicker.Tick+=RingTick;EvaluateSleep();}
  public void StopSleepWatcher(){sleepTicker.Stop();sleepTicker.Tick-=SleepTick;ringTicker.Stop();ringTicker.Tick-=RingTick;sampler.Dispose();}
  async void SleepTick(object sender,EventArgs e){EvaluateSleep();if(!sleeping||sampling||!IsVisible)return;sampling=true;try{ResourceSnapshot next=DemoMode?DemoResources():await Task.Run(()=>sampler.Sample(Config.SleepAdapterId));if(sleeping&&!closing){resources=next;SetRingTargets();UpdateSleepView();StartRingAnimation();}}catch{}finally{sampling=false;}}
  static ResourceSnapshot DemoResources(){double t=DateTime.Now.Second;return new ResourceSnapshot{Cpu=28+t%9,Gpu=17+t%7,Memory=63.4,Download=4.8*1024*1024,Upload=720*1024,DiskRead=2.1*1024*1024,DiskWrite=820*1024,NetworkName="演示网卡",CpuProcesses=new[]{new ResourcePart("Codex",11),new ResourcePart("浏览器",7),new ResourcePart("系统服务",4)},GpuProcesses=new[]{new ResourcePart("浏览器",9),new ResourcePart("Codex",5),new ResourcePart("桌面窗口",3)},MemoryProcesses=new[]{new ResourcePart("浏览器",17),new ResourcePart("Codex",11),new ResourcePart("桌面窗口",5)}};}
  public void ShowSleepDemo(){resources=DemoResources();SetSleeping(true);}
  public bool TestSleepClick(){if(sleepLayer==null||taskPage==null)return false;var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent};sleepLayer.RaiseEvent(click);return !sleeping&&sleepLayer.Visibility==Visibility.Collapsed&&taskPage.Visibility==Visibility.Visible;}
  public void EvaluateSleep(){if(DemoMode)return;if(!Config.SleepAuto){if(sleeping)WakeSleep();quietSince=DateTime.MinValue;return;}if(PanelOpen){quietSince=DateTime.UtcNow;return;}bool quiet=SleepPolicy.Quiet(Engine.Connected,Engine.Cards.Values);if(!quiet){quietSince=DateTime.MinValue;if(sleeping)WakeSleep();return;}if(quietSince==DateTime.MinValue)quietSince=DateTime.UtcNow;if(!sleeping&&SleepPolicy.Due(quietSince,DateTime.UtcNow,Config.SleepIdleMinutes))SetSleeping(true);}
  public void WakeSleep(){if(sleeping)SetSleeping(false);quietSince=DateTime.UtcNow;}
  void SetSleeping(bool value){if(sleeping==value)return;sleeping=value;if(taskPage!=null)taskPage.Visibility=value?Visibility.Collapsed:Visibility.Visible;if(sleepLayer!=null)sleepLayer.Visibility=value?Visibility.Visible:Visibility.Collapsed;if(value){SetRingTargets();UpdateSleepView();StartRingAnimation();}else{ringTicker.Stop();lastRingFrame=DateTime.MinValue;}}
  public void RefreshSleepAppearance(){if(sleepLayer==null)return;BuildSleepBackground();SetRingTargets();UpdateSleepView();StartRingAnimation();}
  public void BuildSleepLayer(Grid root,Border dashboard){taskPage=dashboard;sleepLayer=new Grid{Visibility=sleeping?Visibility.Visible:Visibility.Collapsed,ClipToBounds=true,Cursor=Cursors.Hand};root.Children.Add(sleepLayer);sleepLayer.PreviewMouseLeftButtonDown+=(s,e)=>{WakeSleep();e.Handled=true;};BuildSleepBackground();UpdateSleepView();taskPage.Visibility=sleeping?Visibility.Collapsed:Visibility.Visible;}

  string SleepInk {get{return sleepWallpaperActive?"#FFFFFF":Design.Ink;}}
  string SleepMuted {get{return sleepWallpaperActive?"#D5D8E5":Design.Muted;}}
  string SleepLine {get{return sleepWallpaperActive?"#5B6071":Design.Line;}}
  Brush Accent(){string color=Config.SleepAccent=="green"?(sleepWallpaperActive||Design.Dark?"#80D4AC":"#28754F"):Config.SleepAccent=="orange"?(sleepWallpaperActive||Design.Dark?"#F0BE75":"#A56113"):Config.SleepAccent=="purple"?(sleepWallpaperActive||Design.Dark?"#AAA7FF":"#5856D6"):(sleepWallpaperActive||Design.Dark?"#84C9FA":"#3479B8");return Design.B(color);}

  void BuildSleepBackground(){
   sleepLayer.Children.Clear();sleepWallpaperActive=false;
   bool portrait=ActualHeight>0?Portrait:Height>Width;sleepPortraitLayout=portrait;
   Brush wallpaper=null;
   try{if(!string.IsNullOrWhiteSpace(Config.SleepWallpaperPath)&&File.Exists(Config.SleepWallpaperPath)){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(Path.GetFullPath(Config.SleepWallpaperPath));bitmap.EndInit();bitmap.Freeze();wallpaper=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Center};sleepWallpaperActive=true;}}catch{}
   sleepLayer.Background=Design.B(Design.Canvas);
   var surface=new Border{CornerRadius=new CornerRadius(12),BorderBrush=Design.B(sleepWallpaperActive?"#5B6071":Design.Line),BorderThickness=new Thickness(1),Background=sleepWallpaperActive?wallpaper:Design.B(Design.Surface)};
   if(sleepWallpaperActive)surface.Effect=new BlurEffect{Radius=12};
   sleepLayer.Children.Add(surface);
   if(sleepWallpaperActive){byte alpha=(byte)Math.Round(255*Math.Max(.2,Math.Min(.8,Config.SleepDim)));sleepLayer.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(alpha,7,10,20)),Margin=new Thickness(1)});}
   var content=new Grid{Margin=new Thickness(18,12,18,16)};
   content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(portrait?52:38)});
   content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
   sleepLayer.Children.Add(content);
   var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});if(!portrait)header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var left=new StackPanel{Orientation=portrait?Orientation.Vertical:Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   var titleRow=new StackPanel{Orientation=Orientation.Horizontal};
   titleRow.Children.Add(new Border{Width=7,Height=7,CornerRadius=new CornerRadius(4),Background=Accent(),Margin=new Thickness(0,0,10,0)});
   var name=Design.Text(DemoMode?"静息监控 · 演示":"静息监控",15,SleepInk);name.FontWeight=FontWeights.SemiBold;titleRow.Children.Add(name);left.Children.Add(titleRow);
   var hint=Design.Text("点击任意位置返回任务",11,SleepMuted);hint.Margin=portrait?new Thickness(0,2,0,0):new Thickness(14,1,0,0);left.Children.Add(hint);
   header.Children.Add(left);
   sleepClock=Design.Text("",portrait?16:20,SleepInk);sleepClock.FontWeight=FontWeights.SemiBold;sleepClock.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(sleepClock,1);header.Children.Add(sleepClock);
   if(!portrait){var back=new Border{CornerRadius=new CornerRadius(7),BorderBrush=Design.B(SleepLine),BorderThickness=new Thickness(1),Padding=new Thickness(12,5,12,5),Margin=new Thickness(18,0,0,0),Child=Design.Text("返回任务  ↗",12,SleepInk)};Grid.SetColumn(back,2);header.Children.Add(back);}
   content.Children.Add(new Border{CornerRadius=new CornerRadius(8),Background=sleepWallpaperActive?new SolidColorBrush(Color.FromArgb(220,15,18,30)):Brushes.Transparent,Child=header});
   sleepTiles=new UniformGrid{Columns=1,Rows=1};
   var metricScroll=new ScrollViewer{Content=sleepTiles,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Brushes.Transparent};
   var metricSurface=new Border{Background=sleepWallpaperActive?new SolidColorBrush(Color.FromArgb(210,15,18,30)):Design.B(Design.Surface),CornerRadius=new CornerRadius(8),BorderBrush=Design.B(SleepLine),BorderThickness=new Thickness(1),Child=metricScroll};
   Grid.SetRow(metricSurface,1);content.Children.Add(metricSurface);
  }

  string Speed(double? bytes){if(!bytes.HasValue)return "—";double value=Math.Max(0,bytes.Value);if(Config.SleepSpeedUnit=="mb")return (value/1048576).ToString("0.0")+" MB/s";if(Config.SleepSpeedUnit=="mbps")return (value*8/1000000).ToString("0.0")+" Mb/s";if(value>=1048576)return (value/1048576).ToString("0.0")+" MB/s";if(value>=1024)return (value/1024).ToString("0.0")+" KB/s";return value.ToString("0")+" B/s";}
  string Percent(double? value){return value.HasValue?value.Value.ToString("0")+"%":"—";}
  RingMetricMotion RingState(string id){RingMetricMotion state;if(!ringMotions.TryGetValue(id,out state)){state=new RingMetricMotion();ringMotions[id]=state;}return state;}
  void SetRingTargets(){RingState("cpu").SetTarget(resources.Cpu,resources.CpuProcesses);RingState("gpu").SetTarget(resources.Gpu,resources.GpuProcesses);RingState("memory").SetTarget(resources.Memory,resources.MemoryProcesses);if(!Config.SleepSmoothRings)foreach(var state in ringMotions.Values)state.Snap();}
  void StartRingAnimation(){if(!sleeping||!Config.SleepRingCharts||!Config.SleepSmoothRings||!IsVisible||ringVisuals.Count==0){ringTicker.Stop();return;}lastRingFrame=DateTime.UtcNow;if(!ringTicker.IsEnabled)ringTicker.Start();}
  void RingTick(object sender,EventArgs e){if(!sleeping||!Config.SleepRingCharts||!Config.SleepSmoothRings||!IsVisible){ringTicker.Stop();lastRingFrame=DateTime.MinValue;return;}DateTime now=DateTime.UtcNow;double seconds=lastRingFrame==DateTime.MinValue?.033:(now-lastRingFrame).TotalSeconds;lastRingFrame=now;bool moving=false;foreach(var pair in ringMotions)moving=pair.Value.Step(seconds)||moving;foreach(var pair in ringVisuals)DrawRing(pair.Key,pair.Value);if(!moving)ringTicker.Stop();}
  static Geometry Arc(double center,double radius,double start,double sweep){double a=start*Math.PI/180,b=(start+Math.Min(359.8,sweep))*Math.PI/180;var figure=new PathFigure{StartPoint=new Point(center+radius*Math.Cos(a),center+radius*Math.Sin(a)),IsClosed=false};figure.Segments.Add(new ArcSegment{Point=new Point(center+radius*Math.Cos(b),center+radius*Math.Sin(b)),Size=new Size(radius,radius),SweepDirection=SweepDirection.Clockwise,IsLargeArc=sweep>180});return new PathGeometry(new[]{figure});}
  static Color Shade(Color baseColor,int index){double mix=new[]{0,.18,.32,.46,.6}[Math.Min(4,index)];return Color.FromRgb((byte)Math.Round(baseColor.R+(255-baseColor.R)*mix),(byte)Math.Round(baseColor.G+(255-baseColor.G)*mix),(byte)Math.Round(baseColor.B+(255-baseColor.B)*mix));}
  static void RingBase(Canvas canvas,double center,double radius,double thickness,Brush color){var circle=new System.Windows.Shapes.Ellipse{Width=radius*2,Height=radius*2,Stroke=color,StrokeThickness=thickness};Canvas.SetLeft(circle,center-radius);Canvas.SetTop(circle,center-radius);canvas.Children.Add(circle);}
  static void RingArc(Canvas canvas,double center,double radius,double thickness,double start,double sweep,Brush color,string tip){if(sweep<=.2)return;double gap=Math.Min(1.2,sweep/8);var segment=new System.Windows.Shapes.Path{Data=Arc(center,radius,start+gap,sweep-2*gap),Stroke=color,StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Flat,StrokeEndLineCap=PenLineCap.Flat,ToolTip=tip};canvas.Children.Add(segment);}
  void DrawRing(string id,RingVisual visual){
   var state=RingState(id);var canvas=visual.Canvas;double diameter=visual.Diameter,center=diameter/2,radius=center-11,thickness=diameter>=120?14:10;
   canvas.Children.Clear();var baseColor=((SolidColorBrush)Accent()).Color;RingBase(canvas,center,radius,thickness,Design.B(SleepLine));
   double used=Math.Max(0,Math.Min(100,state.Total.Value)),offset=-90,sum=0;int index=0;
   foreach(string name in state.Order){var motion=state.Parts[name];double value=Math.Max(0,Math.Min(used-sum,motion.Value));if(value<=0)continue;RingArc(canvas,center,radius,thickness,offset,value*3.6,new SolidColorBrush(Shade(baseColor,index++)),name+" "+value.ToString("0.#")+"%");offset+=value*3.6;sum+=value;}
   double other=Math.Max(0,used-sum);RingArc(canvas,center,radius,thickness,offset,other*3.6,new SolidColorBrush(Shade(baseColor,index==0?0:4)),"其他程序 "+other.ToString("0.#")+"%");
   visual.Label.Text=Percent(state.Available?(double?)used:null);
  }
  FrameworkElement RingChart(string id,double diameter){
   var chart=new Grid{Width=diameter,Height=diameter,HorizontalAlignment=HorizontalAlignment.Center};var canvas=new Canvas{Width=diameter,Height=diameter};chart.Children.Add(canvas);
   var amount=Design.Text("—",diameter>=120?27:18,SleepInk);amount.FontWeight=FontWeights.SemiBold;amount.HorizontalAlignment=HorizontalAlignment.Center;amount.VerticalAlignment=VerticalAlignment.Center;chart.Children.Add(amount);
   var visual=new RingVisual{Canvas=canvas,Label=amount,Diameter=diameter};ringVisuals[id]=visual;DrawRing(id,visual);
   return chart;
  }
  UIElement RingLegend(ResourcePart[] parts,bool compact){var legend=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=compact?new Thickness(14,0,0,0):new Thickness(0,3,0,0)};if(parts==null||parts.Length==0){legend.Children.Add(Design.Text("分程序数据读取中",10,SleepMuted));return legend;}var baseColor=((SolidColorBrush)Accent()).Color;for(int i=0;i<Math.Min(3,parts.Length);i++){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new Border{Width=6,Height=6,CornerRadius=new CornerRadius(3),Background=new SolidColorBrush(Shade(baseColor,i)),Margin=new Thickness(0,0,6,0)});var label=Design.Text(parts[i].Name+"  "+parts[i].Percent.ToString("0.#")+"%",10,SleepMuted);label.MaxWidth=compact?(Portrait?178:80):208;row.Children.Add(label);legend.Children.Add(row);}return legend;}
  void AddRingMetric(StackPanel values,string id,ResourcePart[] parts){bool compact=Portrait,shortWindow=UiHeight<245;double diameter=compact?94:shortWindow?88:126;var chart=RingChart(id,diameter);if(compact||shortWindow){var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(chart);row.Children.Add(RingLegend(parts,true));values.Children.Add(row);}else{values.Children.Add(chart);values.Children.Add(RingLegend(parts,false));}}
  void AddMetric(string id,int index,int columns){
   string label=id=="cpu"?"CPU":id=="gpu"?"GPU":id=="memory"?"内存":id=="network"?"网络":"磁盘";
   var cell=new Border{BorderBrush=Design.B(SleepLine),BorderThickness=new Thickness(index%columns==0?0:1,index<columns?0:1,0,0),Padding=new Thickness(18,10,18,10)};
   var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});cell.Child=grid;
   var title=Design.Text(label,12,SleepMuted);title.FontWeight=FontWeights.Medium;grid.Children.Add(title);
   var values=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(values,1);grid.Children.Add(values);
   if(id=="cpu"||id=="gpu"||id=="memory"){
    double? amount=id=="cpu"?resources.Cpu:id=="gpu"?resources.Gpu:resources.Memory;
    if(Config.SleepRingCharts)AddRingMetric(values,id,id=="cpu"?resources.CpuProcesses:id=="gpu"?resources.GpuProcesses:resources.MemoryProcesses);
    else{var number=Design.Text(Percent(amount),34,SleepInk);number.FontWeight=FontWeights.SemiBold;values.Children.Add(number);var rail=new Border{Height=3,CornerRadius=new CornerRadius(2),Background=Design.B(SleepLine),Margin=new Thickness(0,12,0,0)};var fill=new Border{Height=3,CornerRadius=new CornerRadius(2),Background=Accent(),HorizontalAlignment=HorizontalAlignment.Left};rail.Child=fill;rail.SizeChanged+=(s,e)=>fill.Width=Math.Max(0,rail.ActualWidth*(amount??0)/100);values.Children.Add(rail);}
   }else{
    var primary=new StackPanel{Orientation=Orientation.Horizontal};
    primary.Children.Add(Design.Text(id=="network"?"下载":"读取",11,SleepMuted));
    var first=Design.Text(Speed(id=="network"?resources.Download:resources.DiskRead),24,SleepInk);first.FontWeight=FontWeights.SemiBold;first.Margin=new Thickness(10,0,0,0);primary.Children.Add(first);values.Children.Add(primary);
    var secondary=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};
    secondary.Children.Add(Design.Text(id=="network"?"上传":"写入",11,SleepMuted));
    var second=Design.Text(Speed(id=="network"?resources.Upload:resources.DiskWrite),17,SleepMuted);second.Margin=new Thickness(10,0,0,0);secondary.Children.Add(second);values.Children.Add(secondary);
   }
   sleepTiles.Children.Add(cell);
  }
  void UpdateSleepView(){if(sleepTiles==null)return;if(IsLoaded&&sleepPortraitLayout!=Portrait)BuildSleepBackground();if(sleepClock!=null)sleepClock.Text=DateTime.Now.ToString("HH:mm");sleepTiles.Children.Clear();ringVisuals.Clear();var ids=Config.SleepWidgets.Where(Settings.SleepWidgetIds.Contains).Distinct().ToArray();if(ids.Length==0)ids=new[]{"cpu"};int columns=Portrait?(UiWidth>=560?2:1):ids.Length;sleepTiles.Columns=Math.Max(1,columns);sleepTiles.Rows=(int)Math.Ceiling(ids.Length/(double)columns);sleepTiles.Width=Math.Max(1,Portrait?UiWidth-42:Math.Max(UiWidth-42,ids.Length*228));for(int i=0;i<ids.Length;i++)AddMetric(ids[i],i,columns);}
 }
}
