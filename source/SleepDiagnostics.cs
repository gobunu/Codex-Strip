using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CodexStrip {
 public sealed class SleepBlocker {
  public Card Card;
  public string[] Reasons;
 }
 public sealed class SleepIgnoreMatch {
  public Card Card;
  public string Keyword;
 }

 public sealed class SleepDiagnostic {
  public SleepBlocker[] Blockers;
  public SleepIgnoreMatch[] Ignored;
  public string Reason;
  public int? RemainingSeconds;
  public int IdleMinutes;
  public bool Connected;
  public static List<string> ParseKeywords(string value){return (value??"").Split(new[]{',','，',';','；','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}
  public static readonly string[] SourceIds={"durable","local","remote-ssh","remote-control"};
  public static List<string> ParseSources(IEnumerable<string> values){return (values??new string[0]).Select(s=>(s??"").Trim().ToLowerInvariant()).Where(SourceIds.Contains).Distinct().ToList();}
  public static string SourceId(string host){host=host??"";if(host.StartsWith("remote-ssh-discovered:",StringComparison.OrdinalIgnoreCase))return "remote-ssh";if(host.StartsWith("remote-control:",StringComparison.OrdinalIgnoreCase))return "remote-control";return host.ToLowerInvariant();}
  public static string SourceLabel(string source){switch(source){case "durable":return UiText.T("云端任务（durable）","Cloud tasks (durable)");case "local":return UiText.T("本机","Local");case "remote-ssh":return UiText.T("SSH 远程","SSH remote");case "remote-control":return "Remote Control";default:return source;}}
  public static string IgnoredBy(Card card,IEnumerable<string> keywords,IEnumerable<string> sources=null){string source=SourceId(card.Host);if(ParseSources(sources).Contains(source))return UiText.T("来源：","Source: ")+SourceLabel(source);return (keywords??new string[0]).Select(s=>(s??"").Trim()).FirstOrDefault(s=>s.Length>0&&(card.Title??"").IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0)??"";}
  public static string[] BlockingReasons(Card card){
   var reasons=new List<string>();
   if(card.QuestionPending)reasons.Add("question_pending");
   switch(card.Status??""){
    case "completed":
     if(card.Unread)reasons.Add("unread_result");
     if(card.CompletionPending)reasons.Add("completion_pending");
     break;
    case "idle":case "notLoaded":case "interrupted":case "failed":break;
    case "active":reasons.Add("running_task");break;
    case "reconnecting":reasons.Add("reconnecting");break;
    case "input":reasons.Add("waiting_input");break;
    case "approval":reasons.Add("waiting_approval");break;
    case "systemError":reasons.Add("error");break;
    default:reasons.Add("unknown_status");break;
   }
   return reasons.ToArray();
  }
  public static SleepDiagnostic Create(bool connected,IEnumerable<Card> cards,bool autoEnabled,bool sleeping,bool manual,DateTime quietSince,DateTime now,int minutes,IEnumerable<string> ignoredKeywords=null,IEnumerable<string> ignoredSources=null){
   var all=cards.ToArray();var keywords=(ignoredKeywords??new string[0]).ToArray();
   var sources=ParseSources(ignoredSources);
   var ignored=all.Select(c=>new SleepIgnoreMatch{Card=c,Keyword=IgnoredBy(c,keywords,sources)}).Where(b=>b.Keyword.Length>0).OrderBy(b=>b.Card.Key,StringComparer.Ordinal).ToArray();
   var blockers=all.Where(c=>IgnoredBy(c,keywords,sources).Length==0).Select(c=>new SleepBlocker{Card=c,Reasons=BlockingReasons(c)}).Where(b=>b.Reasons.Length>0).OrderBy(b=>b.Card.Host,StringComparer.Ordinal).ThenBy(b=>b.Card.Id,StringComparer.Ordinal).ToArray();
   string reason=sleeping?(manual?"manual_sleep":"sleeping"):!autoEnabled?"disabled":!connected?"disconnected":blockers.Any(b=>b.Reasons.Any(r=>r=="running_task"||r=="reconnecting"))?"running_task":blockers.Any(b=>b.Reasons.Any(r=>r=="question_pending"||r=="waiting_input"||r=="waiting_approval"))?"pending_request":blockers.Any(b=>b.Reasons.Any(r=>r=="unread_result"||r=="completion_pending"))?"unread_result":blockers.Length>0?"unknown_or_error":"counting_down";
   int delay=Math.Max(1,minutes);
   int? remaining=reason=="counting_down"?(int?)Math.Ceiling(Math.Max(0,delay*60-(quietSince==DateTime.MinValue?0:Math.Max(0,(now-quietSince).TotalSeconds)))):null;
   return new SleepDiagnostic{Blockers=blockers,Ignored=ignored,Reason=reason,RemainingSeconds=remaining,IdleMinutes=delay,Connected=connected};
  }
  public string Summary {get{
   if(RemainingSeconds.HasValue)return UiText.T("静息倒计时 ","Idle in ")+FormatCountdown(RemainingSeconds.Value);
   switch(Reason){
    case "disabled":return UiText.T("自动静息已关闭","Auto idle is off");
    case "disconnected":return UiText.T("静息暂停 · Codex 未连接","Idle paused · Codex disconnected");
    case "manual_sleep":return UiText.T("已手动进入静息","Manual idle monitor");
    case "sleeping":return UiText.T("已进入静息","Idle monitor active");
    default:return UiText.T("静息暂停 · ","Idle paused · ")+Blockers.Length+UiText.T(" 条线程阻塞"," blocking conversation(s)");
   }
  }}
  public static string FormatCountdown(int seconds){return (seconds/60).ToString("00")+":"+(seconds%60).ToString("00");}
  public static string ReasonLabel(string reason){
   switch(reason){
    case "running_task":return UiText.T("任务运行中","Task running");
    case "reconnecting":return UiText.T("线程正在重连","Conversation reconnecting");
    case "question_pending":return UiText.T("有待回答的提问","Question awaiting an answer");
    case "waiting_input":return UiText.T("等待用户输入","Awaiting input");
    case "waiting_approval":return UiText.T("等待用户确认","Awaiting approval");
    case "unread_result":return UiText.T("完成结果未查看","Unread result");
    case "completion_pending":return UiText.T("完成提醒尚未确认","Completion awaiting review");
    case "error":return UiText.T("线程发生错误","Conversation error");
    default:return UiText.T("线程状态未知","Unknown conversation status");
   }
  }
 }

 public partial class StripWindow {
  TextBlock idleStatus;FrameworkElement idleStatusRow;RowDefinition idleStatusRowDefinition;
  double TaskBodyHeight {get{return Math.Max(0,UiHeight-(Config.SleepDebug?80:56));}}
  public SleepDiagnostic CurrentSleepDiagnostic(){return SleepDiagnostic.Create(Engine.Connected,Engine.Cards.Values,Config.SleepAuto,sleeping,manualSleepCards!=null,quietSince,DateTime.UtcNow,Config.SleepIdleMinutes,Config.SleepIgnoreKeywords,Config.SleepIgnoreSources);}
  FrameworkElement BuildSleepStatusRow(){
   var row=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,4)};
   var debug=Design.Button(UiText.T("静息 Debug","Idle Debug"));debug.MinHeight=20;debug.Padding=new Thickness(6,0,6,0);debug.FontSize=11;debug.Foreground=Design.B(Design.Accent);debug.ToolTip=UiText.T("查看阻止静息的线程和倒计时","Inspect idle blockers and countdown");debug.Click+=delegate{ShowSleepDebug();};DockPanel.SetDock(debug,Dock.Right);row.Children.Add(debug);
   idleStatus=Design.Text("",11,Design.Muted);row.Children.Add(idleStatus);idleStatusRow=row;UpdateSleepDiagnostics();return row;
  }
  void UpdateSleepDiagnostics(){
   var diagnostic=CurrentSleepDiagnostic();
   if(idleStatusRow!=null)idleStatusRow.Visibility=Config.SleepDebug?Visibility.Visible:Visibility.Collapsed;
   if(idleStatusRowDefinition!=null)idleStatusRowDefinition.Height=new GridLength(Config.SleepDebug?24:0);
   if(idleStatus!=null){idleStatus.Text=diagnostic.Summary;idleStatus.ToolTip=diagnostic.Summary;}
   var panel=activePanel as SleepDebugPanel;if(panel!=null)panel.Update(diagnostic);
  }
  public void SetSleepDebugEnabled(bool enabled){Config.SleepDebug=enabled;if(!enabled&&activePanel is SleepDebugPanel)ClosePanel();Save();UpdateSleepDiagnostics();ResizePanels();RenderCards();}
  public void ShowSleepDebug(){if(!Config.SleepDebug)return;if(sleeping)WakeSleep();if(activePanel is SleepDebugPanel){ClosePanel();return;}usageWindow=null;settingsWindow=null;new SleepDebugPanel(this).Show();}
 }

 public sealed class SleepIgnoreEditor:StackPanel {
  public SleepIgnoreEditor(StripWindow main){
   Margin=new Thickness(0,4,0,8);
   var sourcesHeading=Design.Text(UiText.T("按来源忽略线程","Ignore conversations by source"),12);sourcesHeading.Margin=new Thickness(0,0,0,5);Children.Add(sourcesHeading);
   var sources=new WrapPanel();Children.Add(sources);
   foreach(string id in SleepDiagnostic.SourceIds){string source=id;var check=new CheckBox{Content=SleepDiagnostic.SourceLabel(source),IsChecked=main.Config.SleepIgnoreSources.Contains(source),FontSize=12,Foreground=Design.B(Design.Ink),Margin=new Thickness(0,4,16,4),VerticalContentAlignment=VerticalAlignment.Center};System.Windows.Automation.AutomationProperties.SetAutomationId(check,"SleepIgnoreSource-"+source);sources.Children.Add(check);RoutedEventHandler change=delegate{if(check.IsChecked==true){if(!main.Config.SleepIgnoreSources.Contains(source))main.Config.SleepIgnoreSources.Add(source);}else main.Config.SleepIgnoreSources.Remove(source);main.Save();main.EvaluateSleep();};check.Checked+=change;check.Unchecked+=change;}
   var sourceHint=Design.Text(UiText.T("勾选的来源不参与静息计时或唤醒，任务卡片仍会显示。","Selected sources do not affect idle timing or wake. Their cards remain visible."),11,Design.Muted);sourceHint.TextWrapping=TextWrapping.Wrap;sourceHint.TextTrimming=TextTrimming.None;sourceHint.Margin=new Thickness(0,5,0,14);Children.Add(sourceHint);
   var heading=Design.Text(UiText.T("按标题关键词忽略线程","Ignore conversations by title"),12);heading.Margin=new Thickness(0,0,0,5);Children.Add(heading);
   var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var input=new TextBox{Text=string.Join(", ",main.Config.SleepIgnoreKeywords),FontSize=12,MinHeight=30,Padding=new Thickness(6,4,6,4),Foreground=Design.B(Design.Ink),Background=Design.B(Design.Surface),BorderBrush=Design.B(Design.Line),VerticalContentAlignment=VerticalAlignment.Center};input.ToolTip=UiText.T("例如 hb, heartbeat；多个关键词用逗号分隔","For example hb, heartbeat. Separate keywords with commas.");System.Windows.Automation.AutomationProperties.SetName(input,UiText.T("静息忽略关键词","Idle ignore keywords"));row.Children.Add(input);
   var apply=Design.Button(UiText.T("应用","Apply"));apply.Margin=new Thickness(6,0,0,0);apply.Padding=new Thickness(8,2,8,2);Grid.SetColumn(apply,1);row.Children.Add(apply);Children.Add(row);
   var hint=Design.Text(UiText.T("标题包含任一关键词即忽略，不区分大小写；仅影响静息计时和唤醒。清空后应用可取消。","Title contains any keyword, ignoring case. Affects idle timing and wake only. Clear and apply to remove."),11,Design.Muted);hint.TextWrapping=TextWrapping.Wrap;hint.TextTrimming=TextTrimming.None;hint.Margin=new Thickness(0,5,0,0);Children.Add(hint);
   apply.Click+=delegate{main.Config.SleepIgnoreKeywords=SleepDiagnostic.ParseKeywords(input.Text);input.Text=string.Join(", ",main.Config.SleepIgnoreKeywords);main.Save();main.EvaluateSleep();};

  }
 }

 public sealed class SleepDebugPanel:InsetPanel {
  TextBlock summary;
  StackPanel details;
  string rendered="";
  public SleepDebugPanel(StripWindow owner):base(owner){Build();}
  static TextBlock Label(string value,double size=12,string color=null){var label=Design.Text(value,size,color);label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.None;label.Margin=new Thickness(0,0,0,5);return label;}
  public override void Build(){
   rendered="";var body=new StackPanel();summary=Label("",17);summary.FontWeight=FontWeights.SemiBold;body.Children.Add(summary);
   body.Children.Add(Label(UiText.T("计时和阻塞原因每秒更新。点击、按键或滚动看板会重新计时。","Updates every second. Clicking, typing, or scrolling in the board resets the timer."),11,Design.Muted));
   details=new StackPanel();body.Children.Add(details);body.Children.Add(new SleepIgnoreEditor(main));Shell(UiText.T("静息 Debug","Idle Debug"),body);Update(main.CurrentSleepDiagnostic());
  }
  public void Update(SleepDiagnostic diagnostic){
   summary.Text=diagnostic.Summary;
   string signature=J.Json(J.Obj("reason",diagnostic.Reason,"minutes",diagnostic.IdleMinutes,"connected",diagnostic.Connected,"ignored",diagnostic.Ignored.Select(b=>J.Obj("key",b.Card.Key,"title",b.Card.Title,"keyword",b.Keyword)).ToArray(),"blockers",diagnostic.Blockers.Select(b=>J.Obj("key",b.Card.Key,"title",b.Card.Title,"status",b.Card.Status,"stale",b.Card.Stale,"error",b.Card.LastError,"reasons",b.Reasons)).ToArray()));
   if(signature==rendered)return;rendered=signature;double offset=bodyScroll==null?0:bodyScroll.VerticalOffset;details.Children.Clear();
   details.Children.Add(Label(diagnostic.IdleMinutes+UiText.T(" 分钟连续空闲 · 检查全部已监控线程（含隐藏）"," minutes idle · Checks all monitored conversations, including hidden cards"),11,Design.Muted));
   if(diagnostic.Reason=="disconnected")details.Children.Add(Label(UiText.T("等待 Codex 连接恢复后开始计时。","The countdown starts after Codex reconnects."),12,Design.Amber));
   if(diagnostic.Reason=="disabled")details.Children.Add(Label(UiText.T("可在设置 → 休眠监控中开启自动静息。","Enable Auto idle in Settings → Idle monitor."),12,Design.Amber));
   if(diagnostic.Blockers.Length==0)details.Children.Add(Label(UiText.T("没有阻塞静息的线程。","No conversations are blocking idle mode."),12,Design.Green));
   foreach(var blocker in diagnostic.Blockers){
    var card=blocker.Card;var rows=new StackPanel();var title=Design.Button(Label(string.IsNullOrWhiteSpace(card.Title)?card.Id:card.Title,13));title.Padding=new Thickness(0);title.HorizontalContentAlignment=HorizontalAlignment.Left;title.ToolTip=UiText.T("查看线程预览","Preview conversation");title.Click+=delegate{main.ShowPreview(card);};rows.Children.Add(title);
    rows.Children.Add(Label(string.Join(UiText.T("；","; "),blocker.Reasons.Select(SleepDiagnostic.ReasonLabel)),12,Design.Amber));
    rows.Children.Add(Label((card.Host=="local"?UiText.T("本机","Local"):card.Host)+" · "+(card.Status??"unknown")+(card.Stale?UiText.T(" · 状态待同步"," · Sync pending"):""),11,Design.Muted));
    rows.Children.Add(Label(UiText.T("线程 ID：","Thread ID: ")+card.Id,10,Design.Muted));
    if(!string.IsNullOrWhiteSpace(card.LastError))rows.Children.Add(Label(card.LastError,11,Design.Muted));
    details.Children.Add(new Border{Child=rows,BorderBrush=Design.B(Design.Line),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,4,0,4)});
   }
   if(diagnostic.Ignored.Length>0){
    details.Children.Add(Label(UiText.T("已忽略的线程（","Ignored conversations (")+diagnostic.Ignored.Length+UiText.T("）",")"),12,Design.Green));
    foreach(var ignored in diagnostic.Ignored){var label=Label((string.IsNullOrWhiteSpace(ignored.Card.Title)?ignored.Card.Id:ignored.Card.Title)+UiText.T(" · 命中："," · Matched: ")+ignored.Keyword,11,Design.Muted);label.ToolTip=ignored.Card.Key;details.Children.Add(label);}
   }
   if(bodyScroll!=null)bodyScroll.ScrollToVerticalOffset(offset);
  }
 }
}
