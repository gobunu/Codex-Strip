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
  public static bool NewAttention(Card before,Card after){
   if(after.Stale)return false;
   bool active=new[]{"active","input","approval","reconnecting"}.Contains(after.Status),alert=after.QuestionPending||after.UnreadResult||new[]{"input","approval","reconnecting","failed","systemError"}.Contains(after.Status);
   if(before==null)return active||alert;
   if(after.TurnId.Length>0&&after.TurnId!=before.TurnId&&(active||alert))return true;
   return (after.QuestionPending&&(!before.QuestionPending||after.Message!=before.Message))||(after.UnreadResult&&!before.UnreadResult)||(alert&&after.Status!=before.Status);
  }
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

 public sealed class RateMotion {
  public double Value,Velocity,Target;public bool Available;
  public bool Step(double seconds){
   double dt=Math.Max(0,Math.Min(.05,seconds));
   double span=Math.Max(1024,Math.Max(Value,Target));
   double acceleration=Math.Max(-12*span,Math.Min(12*span,36*(Target-Value)-10*Velocity));
   Velocity=Math.Max(-3*span,Math.Min(3*span,Velocity+acceleration*dt));
   double next=Value+Velocity*dt;
   if((Target-Value)*(Target-next)<0){Value=Target;Velocity=0;}
   else Value=Math.Max(0,next);
   if(Math.Abs(Target-Value)<Math.Max(1,span*.0003)&&Math.Abs(Velocity)<Math.Max(1,span*.002)){Value=Target;Velocity=0;}
   return Velocity!=0||Math.Abs(Target-Value)>=1;
  }
  public void Snap(){Value=Target;Velocity=0;}
 }

 public sealed class RatePoint {public DateTime Time;public double First,Second;public RatePoint(DateTime time,double first,double second){Time=time;First=first;Second=second;}}
 public sealed class RateReading {public DateTime Time;public double? First,Second;public RateReading(DateTime time,double? first,double? second){Time=time;First=first;Second=second;}}
 public sealed class RateTrace {
  public const double WindowSeconds=10;
  public readonly RateMotion First=new RateMotion(),Second=new RateMotion();
  public readonly RateMotion Scale=new RateMotion{Value=256*1024*1.12,Target=256*1024*1.12};
  public readonly List<RatePoint> History=new List<RatePoint>();
  readonly List<RateReading> readings=new List<RateReading>();
  DateTime displayUpdated=DateTime.MinValue;
  public double? DisplayFirst,DisplaySecond;
  public void SetTarget(double? first,double? second,DateTime now,bool record){
   if(record&&(first.HasValue||second.HasValue)){
    if(History.Count>0&&(now-History[History.Count-1].Time).TotalSeconds>WindowSeconds)History.Clear();
    History.Add(new RatePoint(now,First.Value,Second.Value));
    // Keep the point just outside the left edge so its curve can leave through the clip.
    while(History.Count>2&&(now-History[1].Time).TotalSeconds>WindowSeconds)History.RemoveAt(0);
   }
   First.Available=first.HasValue&&!double.IsNaN(first.Value)&&!double.IsInfinity(first.Value);Second.Available=second.HasValue&&!double.IsNaN(second.Value)&&!double.IsInfinity(second.Value);
   First.Target=First.Available?Math.Max(0,first.Value):0;Second.Target=Second.Available?Math.Max(0,second.Value):0;
   UpdateScale(now);
   if(record){
    readings.Add(new RateReading(now,First.Available?(double?)First.Target:null,Second.Available?(double?)Second.Target:null));readings.RemoveAll(p=>(now-p.Time).TotalSeconds>=5);
    if(displayUpdated==DateTime.MinValue||(now-displayUpdated).TotalSeconds>=3||(!DisplayFirst.HasValue&&First.Available)||(!DisplaySecond.HasValue&&Second.Available)){
     DisplayFirst=readings.Select(p=>p.First).Average();DisplaySecond=readings.Select(p=>p.Second).Average();displayUpdated=now;
    }
   }
  }
  public RatePoint[] VisibleHistory(DateTime now){int start=0;while(start+1<History.Count&&(now-History[start+1].Time).TotalSeconds>=WindowSeconds)start++;return History.Skip(start).ToArray();}
  public void UpdateScale(DateTime now){double peak=Math.Max(256*1024,Math.Max(Math.Max(First.Value,Second.Value),Math.Max(First.Target,Second.Target)));foreach(var point in VisibleHistory(now))peak=Math.Max(peak,Math.Max(point.First,point.Second));Scale.Target=peak*1.12;}
  public bool Step(double seconds){
   bool first=First.Step(seconds),second=Second.Step(seconds);double previous=Scale.Value;bool scale=Scale.Step(seconds);
   // Limit the relative fall of the axis: a large departing spike must not make
   // small remaining values suddenly expand near the end of the transition.
   double floor=previous*Math.Exp(-.7*Math.Max(0,Math.Min(.05,seconds)));
   if(Scale.Value<floor){Scale.Value=floor;Scale.Velocity=Math.Max(Scale.Velocity,-.7*floor);scale=true;}
   return first||second||scale;
  }
  public void Snap(){First.Snap();Second.Snap();Scale.Snap();}
 }

 public partial class StripWindow {
  sealed class RingVisual {public Canvas Canvas;public TextBlock Label;public double Diameter;}
  sealed class RateVisual {public Canvas Canvas;public double Width,Height;public readonly System.Windows.Shapes.Path[] Curves=new System.Windows.Shapes.Path[2];}
  Grid sleepLayer,wallpaperScene;BitmapSource sleepWallpaperBitmap;Border taskPage;UniformGrid sleepTiles;TextBlock sleepClock;
  readonly DispatcherTimer sleepTicker=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
  readonly DispatcherTimer ringTicker=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
  readonly Dictionary<string,RingMetricMotion> ringMotions=new Dictionary<string,RingMetricMotion>();
  readonly Dictionary<string,RingVisual> ringVisuals=new Dictionary<string,RingVisual>();
  readonly Dictionary<string,RateTrace> rateTraces=new Dictionary<string,RateTrace>();
  readonly Dictionary<string,RateVisual> rateVisuals=new Dictionary<string,RateVisual>();
  readonly Dictionary<string,Border> sleepCells=new Dictionary<string,Border>();string sleepTileLayout="";
  DateTime lastRingFrame=DateTime.MinValue;
  readonly ResourceSampler sampler=new ResourceSampler();ResourceSnapshot resources=new ResourceSnapshot();
  DateTime quietSince=DateTime.MinValue;bool sleeping,sampling,sleepWallpaperActive,sleepPortraitLayout;
  Dictionary<string,Card> manualSleepCards;
  public bool Sleeping {get{return sleeping;}}
  public void StartSleepWatcher(){sleepTicker.Tick+=SleepTick;sleepTicker.Start();ringTicker.Tick+=RingTick;EvaluateSleep();}
  public void StopSleepWatcher(){sleepTicker.Stop();sleepTicker.Tick-=SleepTick;ringTicker.Stop();ringTicker.Tick-=RingTick;sampler.Dispose();}
  async void SleepTick(object sender,EventArgs e){EvaluateSleep();if(sleeping&&IsVisible&&sleepClock!=null)sleepClock.Text=DateTime.Now.ToString("HH:mm:ss");if(!sleeping||sampling||!IsVisible)return;sampling=true;try{ResourceSnapshot next=DemoMode?DemoResources():await Task.Run(()=>sampler.Sample(Config.SleepAdapterId));if(sleeping&&!closing){resources=next;SetRingTargets();SetRateTargets(true);UpdateSleepView();StartRingAnimation();}}catch{}finally{sampling=false;}}
  static ResourceSnapshot DemoResources(){double t=DateTime.Now.Second,phase=DateTime.Now.TimeOfDay.TotalSeconds;return new ResourceSnapshot{Cpu=28+t%9,Gpu=17+t%7,Memory=63.4,Download=(3.8+1.5*Math.Sin(phase*1.2))*1024*1024,Upload=(.7+.3*Math.Sin(phase*.8+1))*1024*1024,DiskRead=(2.1+1.2*Math.Sin(phase*.9))*1024*1024,DiskWrite=(.8+.4*Math.Sin(phase*.6+2))*1024*1024,NetworkName="演示网卡",CpuProcesses=new[]{new ResourcePart("Codex",11),new ResourcePart("浏览器",7),new ResourcePart("系统服务",4)},GpuProcesses=new[]{new ResourcePart("浏览器",9),new ResourcePart("Codex",5),new ResourcePart("桌面窗口",3)},MemoryProcesses=new[]{new ResourcePart("浏览器",17),new ResourcePart("Codex",11),new ResourcePart("桌面窗口",5)}};}
  public void ShowSleepDemo(){resources=DemoResources();SetSleeping(true);}
  public void ShowSleepMonitor(){ClosePanel();manualSleepCards=Engine.Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());if(DemoMode)resources=DemoResources();SetSleeping(true);SleepTick(null,EventArgs.Empty);}
  public bool TestSleepClick(){if(sleepLayer==null||taskPage==null)return false;var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent};sleepLayer.RaiseEvent(click);return !sleeping&&sleepLayer.Visibility==Visibility.Collapsed&&taskPage.Visibility==Visibility.Visible;}
  public void EvaluateSleep(){if(DemoMode)return;if(manualSleepCards!=null){foreach(var card in Engine.Cards.Values){Card previous;manualSleepCards.TryGetValue(card.Key,out previous);if(SleepPolicy.NewAttention(previous,card)){WakeSleep();return;}}manualSleepCards=Engine.Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());return;}if(!Config.SleepAuto){if(sleeping)WakeSleep();quietSince=DateTime.MinValue;return;}if(PanelOpen){quietSince=DateTime.UtcNow;return;}bool quiet=SleepPolicy.Quiet(Engine.Connected,Engine.Cards.Values);if(!quiet){quietSince=DateTime.MinValue;if(sleeping)WakeSleep();return;}if(quietSince==DateTime.MinValue)quietSince=DateTime.UtcNow;if(!sleeping&&SleepPolicy.Due(quietSince,DateTime.UtcNow,Config.SleepIdleMinutes))SetSleeping(true);}
  public void WakeSleep(){manualSleepCards=null;if(sleeping)SetSleeping(false);quietSince=DateTime.UtcNow;}
  void SetSleeping(bool value){if(sleeping==value)return;sleeping=value;if(taskPage!=null)taskPage.Visibility=value?Visibility.Collapsed:Visibility.Visible;if(sleepLayer!=null)sleepLayer.Visibility=value?Visibility.Visible:Visibility.Collapsed;if(value){SetRingTargets();SetRateTargets(true);UpdateSleepView();StartRingAnimation();}else{ringTicker.Stop();lastRingFrame=DateTime.MinValue;}}
  public void RefreshSleepAppearance(){if(sleepLayer==null)return;BuildSleepBackground();SetRingTargets();SetRateTargets(false);UpdateSleepView();StartRingAnimation();}
  public void BuildSleepLayer(Grid root,Border dashboard){taskPage=dashboard;sleepLayer=new Grid{Visibility=sleeping?Visibility.Visible:Visibility.Collapsed,ClipToBounds=true,Cursor=Cursors.Hand};root.Children.Add(sleepLayer);sleepLayer.PreviewMouseLeftButtonDown+=(s,e)=>{WakeSleep();e.Handled=true;};BuildSleepBackground();UpdateSleepView();taskPage.Visibility=sleeping?Visibility.Collapsed:Visibility.Visible;}

  string SleepInk {get{return sleepWallpaperActive?"#FFFFFF":Design.Ink;}}
  string SleepMuted {get{return sleepWallpaperActive?"#E9EDF4":Design.Muted;}}
  string SleepLine {get{return sleepWallpaperActive?"#5B6071":Design.Line;}}
  Brush Accent(){string color=Config.SleepAccent=="green"?(sleepWallpaperActive||Design.Dark?"#80D4AC":"#28754F"):Config.SleepAccent=="orange"?(sleepWallpaperActive||Design.Dark?"#F0BE75":"#A56113"):Config.SleepAccent=="purple"?(sleepWallpaperActive||Design.Dark?"#AAA7FF":"#5856D6"):(sleepWallpaperActive||Design.Dark?"#84C9FA":"#3479B8");return Design.B(color);}
  Brush RateColor(int channel){if(channel==0)return Accent();bool dark=sleepWallpaperActive||Design.Dark;return Design.B(Config.SleepAccent=="orange"?(dark?"#85CCFF":"#256FA8"):(dark?"#FFC078":"#A55C12"));}
  TextBlock SleepText(string text,double size=13,string color=null){var label=Design.Text(text,size,color);if(sleepWallpaperActive)label.Effect=new DropShadowEffect{Color=Colors.Black,Opacity=.85,BlurRadius=3,ShadowDepth=1};return label;}
  Brush SleepDivider {get{return sleepWallpaperActive?new SolidColorBrush(Color.FromArgb(48,255,255,255)):Design.B(SleepLine);}}
  Border GlassSurface(UIElement content,double radius){
   var shell=new Border{CornerRadius=new CornerRadius(radius)};var layers=new Grid();shell.Child=layers;
   var glass=new Grid();layers.Children.Add(glass);
   var sceneVisual=wallpaperScene;var bitmap=sleepWallpaperBitmap;
   var backdropBrush=new VisualBrush(sceneVisual){ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill,AutoLayoutContent=false};
   // Overscan before blur so the clipped edge never blends against transparent black.
   var backdrop=new Border{Background=backdropBrush,Margin=new Thickness(-3),Effect=Config.SleepGlassRefraction?new BlurEffect{Radius=1.8,RenderingBias=RenderingBias.Quality}:null,CacheMode=new BitmapCache()};glass.Children.Add(backdrop);
   var edge=new Image{Stretch=Stretch.Fill,IsHitTestVisible=false};glass.Children.Add(edge);
   // The same light film covers both the refracted band and the central backdrop.
   var tint=new LinearGradientBrush();tint.StartPoint=new Point(0,0);tint.EndPoint=new Point(.25,1);tint.GradientStops.Add(new GradientStop(Color.FromArgb(25,255,255,255),0));tint.GradientStops.Add(new GradientStop(Color.FromArgb(3,255,255,255),.45));tint.GradientStops.Add(new GradientStop(Color.FromArgb(8,255,255,255),1));glass.Children.Add(new Border{Background=tint});
   var sheen=new LinearGradientBrush();sheen.StartPoint=new Point(0,0);sheen.EndPoint=new Point(.8,1);sheen.GradientStops.Add(new GradientStop(Color.FromArgb(28,255,255,255),0));sheen.GradientStops.Add(new GradientStop(Colors.Transparent,.35));sheen.GradientStops.Add(new GradientStop(Color.FromArgb(8,255,255,255),1));glass.Children.Add(new Border{Background=sheen});
   var glowBrush=new RadialGradientBrush(Colors.White,Colors.Transparent){RadiusX=.75,RadiusY=.85};var glow=new Border{Background=glowBrush,Opacity=0,IsHitTestVisible=false};glass.Children.Add(glow);
   shell.MouseEnter+=delegate{glow.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(.14,TimeSpan.FromMilliseconds(180)));};shell.MouseLeave+=delegate{glow.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,TimeSpan.FromMilliseconds(220)));};shell.MouseMove+=(sender,e)=>{Point at=e.GetPosition(shell);var center=new Point(at.X/Math.Max(1,shell.ActualWidth),at.Y/Math.Max(1,shell.ActualHeight));glowBrush.Center=center;glowBrush.GradientOrigin=center;};
   var rim=new LinearGradientBrush();rim.StartPoint=new Point(0,0);rim.EndPoint=new Point(.15,1);rim.GradientStops.Add(new GradientStop(Color.FromArgb(190,255,255,255),0));rim.GradientStops.Add(new GradientStop(Color.FromArgb(40,255,255,255),.4));rim.GradientStops.Add(new GradientStop(Color.FromArgb(115,255,255,255),1));
   layers.Children.Add(new Border{CornerRadius=new CornerRadius(radius),BorderBrush=rim,BorderThickness=new Thickness(1),IsHitTestVisible=false});
   layers.Children.Add(new Border{CornerRadius=new CornerRadius(Math.Max(0,radius-1.5)),BorderBrush=new SolidColorBrush(Color.FromArgb(25,255,255,255)),BorderThickness=new Thickness(.5),Margin=new Thickness(1.5),IsHitTestVisible=false});
   layers.Children.Add(content);
   Rect last=Rect.Empty;Size lastScene=Size.Empty;
   Action align=()=>{double width=shell.ActualWidth,height=shell.ActualHeight;if(!shell.IsLoaded||!sceneVisual.IsLoaded||width<=0||height<=0)return;Point origin=shell.TranslatePoint(new Point(0,0),sceneVisual);var bounds=new Rect(origin.X,origin.Y,width,height);var scene=new Size(sceneVisual.ActualWidth,sceneVisual.ActualHeight);if(bounds==last&&scene==lastScene)return;last=bounds;lastScene=scene;backdropBrush.Viewbox=new Rect(origin.X-3,origin.Y-3,width+6,height+6);glass.Clip=new RectangleGeometry(new Rect(0,0,width,height),radius,radius);if(bitmap!=null&&scene.Width>0&&scene.Height>0&&bounds.IntersectsWith(new Rect(0,0,scene.Width,scene.Height)))edge.Source=GlassBackdrop.Edge(bitmap,scene,bounds,radius,Config.SleepDim,Config.UiScale,Config.SleepGlassRefraction,(Color)ColorConverter.ConvertFromString(Design.Canvas));else edge.Source=null;};
   SizeChangedEventHandler sceneChanged=delegate{align();};
   shell.Loaded+=delegate{sceneVisual.SizeChanged+=sceneChanged;align();};shell.Unloaded+=delegate{sceneVisual.SizeChanged-=sceneChanged;};shell.SizeChanged+=delegate{align();};shell.LayoutUpdated+=delegate{align();};return shell;
  }

  void BuildSleepBackground(){
   sleepLayer.Children.Clear();sleepCells.Clear();sleepTileLayout="";sleepWallpaperActive=false;sleepWallpaperBitmap=null;
   bool portrait=ActualHeight>0?Portrait:Height>Width;sleepPortraitLayout=portrait;
   Brush wallpaper=null;
   try{if(!string.IsNullOrWhiteSpace(Config.SleepWallpaperPath)&&File.Exists(Config.SleepWallpaperPath)){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(Path.GetFullPath(Config.SleepWallpaperPath));bitmap.EndInit();bitmap.Freeze();sleepWallpaperBitmap=bitmap;wallpaper=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Center};sleepWallpaperActive=true;}}catch{}
   sleepLayer.Background=Design.B(Design.Canvas);
   var surface=new Border{CornerRadius=new CornerRadius(12),BorderBrush=Design.B(sleepWallpaperActive?"#5B6071":Design.Line),BorderThickness=new Thickness(1),Background=sleepWallpaperActive?wallpaper:Design.B(Design.Surface)};
   wallpaperScene=new Grid{IsHitTestVisible=false};wallpaperScene.Children.Add(surface);sleepLayer.Children.Add(wallpaperScene);
   if(sleepWallpaperActive){byte alpha=(byte)Math.Round(255*Math.Max(.2,Math.Min(.8,Config.SleepDim)));wallpaperScene.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(alpha,7,10,20)),Margin=new Thickness(1)});}
   var content=new Grid{Margin=new Thickness(18,12,18,16)};
   content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(portrait?52:38)});
   content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
   sleepLayer.Children.Add(content);
   var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});if(!portrait)header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var left=new StackPanel{Orientation=portrait?Orientation.Vertical:Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   var titleRow=new StackPanel{Orientation=Orientation.Horizontal};
   titleRow.Children.Add(new Border{Width=7,Height=7,CornerRadius=new CornerRadius(4),Background=Accent(),Margin=new Thickness(0,0,10,0)});
   var name=SleepText(DemoMode?"静息监控 · 演示":"静息监控",15,SleepInk);name.FontWeight=FontWeights.SemiBold;titleRow.Children.Add(name);left.Children.Add(titleRow);
   header.Children.Add(left);
   sleepClock=SleepText("",portrait?16:20,SleepInk);sleepClock.FontWeight=FontWeights.SemiBold;sleepClock.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(sleepClock,1);header.Children.Add(sleepClock);
   if(!portrait){var back=SleepText("返回任务  ↗",12,SleepInk);back.Margin=new Thickness(18,0,2,0);Grid.SetColumn(back,2);header.Children.Add(back);}
   if(sleepWallpaperActive){header.Margin=new Thickness(12,0,10,0);var headerGlass=GlassSurface(header,20);headerGlass.Margin=new Thickness(4,0,4,5);content.Children.Add(headerGlass);}else content.Children.Add(new Border{CornerRadius=new CornerRadius(8),Background=Brushes.Transparent,Child=header});
   sleepTiles=new UniformGrid{Columns=1,Rows=1};
   var metricScroll=new ScrollViewer{Content=sleepTiles,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Brushes.Transparent};
   var metricSurface=sleepWallpaperActive?new Border{Background=Brushes.Transparent,Child=metricScroll}:new Border{Background=Design.B(Design.Surface),CornerRadius=new CornerRadius(8),BorderBrush=Design.B(SleepLine),BorderThickness=new Thickness(1),Child=metricScroll};
   Grid.SetRow(metricSurface,1);content.Children.Add(metricSurface);
  }

  string Speed(double? bytes){if(!bytes.HasValue)return "—";double value=Math.Max(0,bytes.Value);if(Config.SleepSpeedUnit=="mb")return (value/1048576).ToString("0.0")+" MB/s";if(Config.SleepSpeedUnit=="mbps")return (value*8/1000000).ToString("0.0")+" Mb/s";if(value>=1048576)return (value/1048576).ToString("0.0")+" MB/s";if(value>=1024)return (value/1024).ToString("0")+" KB/s";return value.ToString("0")+" B/s";}
  string Percent(double? value){return value.HasValue?value.Value.ToString("0")+"%":"—";}
  RingMetricMotion RingState(string id){RingMetricMotion state;if(!ringMotions.TryGetValue(id,out state)){state=new RingMetricMotion();ringMotions[id]=state;}return state;}
  RateTrace RateState(string id){RateTrace state;if(!rateTraces.TryGetValue(id,out state)){state=new RateTrace();rateTraces[id]=state;}return state;}
  void SetRingTargets(){RingState("cpu").SetTarget(resources.Cpu,resources.CpuProcesses);RingState("gpu").SetTarget(resources.Gpu,resources.GpuProcesses);RingState("memory").SetTarget(resources.Memory,resources.MemoryProcesses);if(!Config.SleepSmoothRings)foreach(var state in ringMotions.Values)state.Snap();}
  void SetRateTargets(bool record){DateTime now=DateTime.UtcNow;RateState("network").SetTarget(resources.Download,resources.Upload,now,record);RateState("disk").SetTarget(resources.DiskRead,resources.DiskWrite,now,record);if(!Config.SleepSmoothRings)foreach(var state in rateTraces.Values)state.Snap();}
  void StartRingAnimation(){if(!sleeping||!Config.SleepSmoothRings||!IsVisible||(ringVisuals.Count==0&&rateVisuals.Count==0)){ringTicker.Stop();return;}lastRingFrame=DateTime.UtcNow;if(!ringTicker.IsEnabled)ringTicker.Start();}
  void RingTick(object sender,EventArgs e){if(!sleeping||!Config.SleepSmoothRings||!IsVisible){ringTicker.Stop();lastRingFrame=DateTime.MinValue;return;}DateTime now=DateTime.UtcNow;double seconds=lastRingFrame==DateTime.MinValue?.033:(now-lastRingFrame).TotalSeconds;lastRingFrame=now;bool moving=false;foreach(var pair in ringMotions)moving=pair.Value.Step(seconds)||moving;foreach(var pair in rateTraces){pair.Value.UpdateScale(now);moving=pair.Value.Step(seconds)||moving;}foreach(var pair in ringVisuals)DrawRing(pair.Key,pair.Value);foreach(var pair in rateVisuals)DrawRateTrace(pair.Key,pair.Value,now);if(!moving&&rateVisuals.Count==0)ringTicker.Stop();}
  static Geometry Arc(double center,double radius,double start,double sweep){double a=start*Math.PI/180,b=(start+Math.Min(359.8,sweep))*Math.PI/180;var figure=new PathFigure{StartPoint=new Point(center+radius*Math.Cos(a),center+radius*Math.Sin(a)),IsClosed=false};figure.Segments.Add(new ArcSegment{Point=new Point(center+radius*Math.Cos(b),center+radius*Math.Sin(b)),Size=new Size(radius,radius),SweepDirection=SweepDirection.Clockwise,IsLargeArc=sweep>180});return new PathGeometry(new[]{figure});}
  static Color Shade(Color baseColor,int index){double mix=new[]{0,.18,.32,.46,.6}[Math.Min(4,index)];return Color.FromRgb((byte)Math.Round(baseColor.R+(255-baseColor.R)*mix),(byte)Math.Round(baseColor.G+(255-baseColor.G)*mix),(byte)Math.Round(baseColor.B+(255-baseColor.B)*mix));}
  static void RingBase(Canvas canvas,double center,double radius,double thickness,Brush color){var circle=new System.Windows.Shapes.Ellipse{Width=radius*2,Height=radius*2,Stroke=color,StrokeThickness=thickness};Canvas.SetLeft(circle,center-radius);Canvas.SetTop(circle,center-radius);canvas.Children.Add(circle);}
  static void RingArc(Canvas canvas,double center,double radius,double thickness,double start,double sweep,Brush color,string tip){if(sweep<=.2)return;double gap=Math.Min(1.2,sweep/8);var segment=new System.Windows.Shapes.Path{Data=Arc(center,radius,start+gap,sweep-2*gap),Stroke=color,StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Flat,StrokeEndLineCap=PenLineCap.Flat,ToolTip=tip};canvas.Children.Add(segment);}
  static Geometry RateCurve(IList<Point> points){if(points.Count<2)return Geometry.Empty;var figure=new PathFigure{StartPoint=points[0],IsClosed=false};for(int i=1;i<points.Count;i++){Point a=points[i-1],b=points[i];double third=(b.X-a.X)/3;figure.Segments.Add(new BezierSegment(new Point(a.X+third,a.Y),new Point(b.X-third,b.Y),b,true));}return new PathGeometry(new[]{figure});}
  void DrawRateTrace(string id,RateVisual visual,DateTime now){
   var state=RateState(id);var canvas=visual.Canvas;
   double width=visual.Width,height=visual.Height;var history=state.VisibleHistory(now);double peak=Math.Max(1,state.Scale.Value);
   if(canvas.Children.Count==0){
    canvas.ClipToBounds=true;
    for(int line=1;line<=2;line++){double y=height*line/3;canvas.Children.Add(new System.Windows.Shapes.Line{X1=0,X2=width,Y1=y,Y2=y,Stroke=Design.B(SleepLine),StrokeThickness=.6,Opacity=.55});}
    for(int channel=0;channel<2;channel++){var curve=new System.Windows.Shapes.Path{Stroke=RateColor(channel),StrokeThickness=2,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,ToolTip=(id=="network"?(channel==0?"下载":"上传"):(channel==0?"读取":"写入"))+" · 最近 10 秒"};visual.Curves[channel]=curve;canvas.Children.Add(curve);}
   }
   for(int channel=0;channel<2;channel++){
    var motion=channel==0?state.First:state.Second;if(!motion.Available&&motion.Value<=0){visual.Curves[channel].Data=Geometry.Empty;continue;}
    var points=new List<Point>();foreach(var item in history){double age=(now-item.Time).TotalSeconds;if(age<0)continue;double value=channel==0?item.First:item.Second;points.Add(new Point(width-age*width/RateTrace.WindowSeconds,height-2-Math.Min(1,value/peak)*(height-4)));}
    points.Add(new Point(width,height-2-Math.Min(1,motion.Value/peak)*(height-4)));
    visual.Curves[channel].Data=RateCurve(points);
   }
  }
  void DrawRing(string id,RingVisual visual){
   var state=RingState(id);var canvas=visual.Canvas;double diameter=visual.Diameter,center=diameter/2,radius=center-11,thickness=diameter>=110?14:10;
   canvas.Children.Clear();var baseColor=((SolidColorBrush)Accent()).Color;RingBase(canvas,center,radius,thickness,Design.B(SleepLine));
   double used=Math.Max(0,Math.Min(100,state.Total.Value)),offset=-90,sum=0;int index=0;
   foreach(string name in state.Order){var motion=state.Parts[name];double value=Math.Max(0,Math.Min(used-sum,motion.Value));if(value<=0)continue;RingArc(canvas,center,radius,thickness,offset,value*3.6,new SolidColorBrush(Shade(baseColor,index++)),name+" "+value.ToString("0.#")+"%");offset+=value*3.6;sum+=value;}
   double other=Math.Max(0,used-sum);RingArc(canvas,center,radius,thickness,offset,other*3.6,new SolidColorBrush(Shade(baseColor,index==0?0:4)),"其他程序 "+other.ToString("0.#")+"%");
   visual.Label.Text=Percent(state.Available?(double?)used:null);
  }
  FrameworkElement RingChart(string id,double diameter){
   var chart=new Grid{Width=diameter,Height=diameter,HorizontalAlignment=HorizontalAlignment.Center};var canvas=new Canvas{Width=diameter,Height=diameter};chart.Children.Add(canvas);
   var amount=SleepText("—",diameter>=120?27:diameter>=110?24:18,SleepInk);amount.FontWeight=FontWeights.SemiBold;amount.HorizontalAlignment=HorizontalAlignment.Center;amount.VerticalAlignment=VerticalAlignment.Center;chart.Children.Add(amount);
   var visual=new RingVisual{Canvas=canvas,Label=amount,Diameter=diameter};ringVisuals[id]=visual;DrawRing(id,visual);
   return chart;
  }
  UIElement RingLegend(ResourcePart[] parts,bool compact){var legend=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=compact?new Thickness(14,0,0,0):new Thickness(0,3,0,0)};if(parts==null||parts.Length==0){legend.Children.Add(SleepText("分程序数据读取中",10,SleepMuted));return legend;}var baseColor=((SolidColorBrush)Accent()).Color;for(int i=0;i<Math.Min(3,parts.Length);i++){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new Border{Width=6,Height=6,CornerRadius=new CornerRadius(3),Background=new SolidColorBrush(Shade(baseColor,i)),Margin=new Thickness(0,0,6,0)});var label=SleepText(parts[i].Name+"  "+parts[i].Percent.ToString("0.#")+"%",10,SleepMuted);label.MaxWidth=compact?(Portrait?178:80):208;row.Children.Add(label);legend.Children.Add(row);}return legend;}
  void AddRingMetric(StackPanel values,string id,ResourcePart[] parts){bool compact=Portrait,shortWindow=UiHeight<245;double diameter=compact?94:shortWindow?88:Math.Max(100,Math.Min(126,UiHeight-152));var chart=RingChart(id,diameter);if(compact||shortWindow){var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(chart);row.Children.Add(RingLegend(parts,true));values.Children.Add(row);}else{values.Children.Add(chart);values.Children.Add(RingLegend(parts,false));}}
  void AddMetric(string id,int index,int columns){
   string label=id=="cpu"?"CPU":id=="gpu"?"GPU":id=="memory"?"内存":id=="network"?"网络":"磁盘";
   Border cell;bool added=!sleepCells.TryGetValue(id,out cell);if(added){cell=new Border{BorderBrush=SleepDivider,BorderThickness=sleepWallpaperActive?new Thickness(0):new Thickness(index%columns==0?0:1,index<columns?0:1,0,0),Padding=sleepWallpaperActive?new Thickness(14,10,14,10):new Thickness(18,10,18,10),ClipToBounds=true};sleepCells[id]=cell;}
   var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});cell.Child=grid;
   var title=SleepText(label,12,SleepMuted);title.FontWeight=FontWeights.Medium;grid.Children.Add(title);
   var values=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(values,1);grid.Children.Add(values);
   if(id=="cpu"||id=="gpu"||id=="memory"){
    double? amount=id=="cpu"?resources.Cpu:id=="gpu"?resources.Gpu:resources.Memory;
    if(Config.SleepRingCharts)AddRingMetric(values,id,id=="cpu"?resources.CpuProcesses:id=="gpu"?resources.GpuProcesses:resources.MemoryProcesses);
    else{var number=SleepText(Percent(amount),34,SleepInk);number.FontWeight=FontWeights.SemiBold;values.Children.Add(number);var rail=new Border{Height=3,CornerRadius=new CornerRadius(2),Background=Design.B(SleepLine),Margin=new Thickness(0,12,0,0)};var fill=new Border{Height=3,CornerRadius=new CornerRadius(2),Background=Accent(),HorizontalAlignment=HorizontalAlignment.Left};rail.Child=fill;rail.SizeChanged+=(s,e)=>fill.Width=Math.Max(0,rail.ActualWidth*(amount??0)/100);values.Children.Add(rail);}
   }else{
    var rate=RateState(id);
    var primary=new StackPanel{Orientation=Orientation.Horizontal};
    primary.Children.Add(new System.Windows.Shapes.Ellipse{Width=6,Height=6,Fill=RateColor(0),Margin=new Thickness(0,0,6,0),VerticalAlignment=VerticalAlignment.Center});
    var firstLabel=SleepText(id=="network"?"下载":"读取",11);firstLabel.Foreground=RateColor(0);primary.Children.Add(firstLabel);
    var first=SleepText(Speed(rate.DisplayFirst),24,SleepInk);first.FontWeight=FontWeights.SemiBold;first.Margin=new Thickness(10,0,0,0);primary.Children.Add(first);values.Children.Add(primary);
    var secondary=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};
    secondary.Children.Add(new System.Windows.Shapes.Ellipse{Width=6,Height=6,Fill=RateColor(1),Margin=new Thickness(0,0,6,0),VerticalAlignment=VerticalAlignment.Center});
    var secondLabel=SleepText(id=="network"?"上传":"写入",11);secondLabel.Foreground=RateColor(1);secondary.Children.Add(secondLabel);
    var second=SleepText(Speed(rate.DisplaySecond),17,SleepMuted);second.Margin=new Thickness(10,0,0,0);secondary.Children.Add(second);values.Children.Add(secondary);
    double width=Math.Max(120,sleepTiles.Width/Math.Max(1,columns)-40),height=UiHeight<245?25:40;
    var graph=new Canvas{Width=width,Height=height,Margin=new Thickness(0,6,0,0),ToolTip="最近 10 秒趋势 · 纵轴按峰值自动缩放"};values.Children.Add(graph);
    var visual=new RateVisual{Canvas=graph,Width=width,Height=height};rateVisuals[id]=visual;DrawRateTrace(id,visual,DateTime.UtcNow);
   }
   if(added){if(sleepWallpaperActive){var panel=GlassSurface(cell,26);panel.Margin=new Thickness(4,index<columns?0:4,4,4);sleepTiles.Children.Add(panel);}else sleepTiles.Children.Add(cell);}
  }
  void UpdateSleepView(){if(sleepTiles==null)return;if(IsLoaded&&sleepPortraitLayout!=Portrait)BuildSleepBackground();if(sleepClock!=null)sleepClock.Text=DateTime.Now.ToString("HH:mm:ss");ringVisuals.Clear();rateVisuals.Clear();var ids=Config.SleepWidgets.Where(Settings.SleepWidgetIds.Contains).Distinct().ToArray();if(ids.Length==0)ids=new[]{"cpu"};int columns=Portrait?(UiWidth>=560?2:1):ids.Length;string layout=columns+"/"+string.Join("/",ids);if(layout!=sleepTileLayout){sleepTiles.Children.Clear();sleepCells.Clear();sleepTileLayout=layout;}sleepTiles.Columns=Math.Max(1,columns);sleepTiles.Rows=(int)Math.Ceiling(ids.Length/(double)columns);sleepTiles.Width=Math.Max(1,Portrait?UiWidth-42:Math.Max(UiWidth-42,ids.Length*228));for(int i=0;i<ids.Length;i++)AddMetric(ids[i],i,columns);}
 }
}
