using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CodexStrip {
 public sealed class Settings {
  public static readonly string[] SleepWidgetIds={"cpu","gpu","memory","network","disk"};
  public bool SleepDebug=false;public bool SleepAuto=true,SleepRingCharts=true,SleepSmoothRings=true,SleepGlassRefraction=false;public int SleepIdleMinutes=5;public List<string> SleepWidgets=new List<string>(SleepWidgetIds);
  public List<string> SleepIgnoreKeywords=new List<string>();public string SleepWallpaperPath="",SleepAdapterId="",SleepSpeedUnit="auto",SleepAccent="blue";public double SleepDim=.4;
  public string ThemeMode="system"; public double UiScale=1.5,DefaultUiScale=1.5; public int Count=4; public bool Topmost=true; public string TopmostMode="always"; public bool ReserveWorkArea=true; public string DockEdge="top"; public string DockMonitor=""; public double DockHorizontalSize=286,DockVerticalSize=320; public string Host="all"; public double Width=1280,Height=286,Left=80,Top=80;
  public Dictionary<string,string> Watched=new Dictionary<string,string>();
  public Dictionary<string,double[]> FloatingRects=new Dictionary<string,double[]>();
  public Dictionary<string,double> Thicknesses=new Dictionary<string,double>();
  public Dictionary<string,double> MonitorScales=new Dictionary<string,double>();
  public Dictionary<string,string> PendingCompletions=new Dictionary<string,string>();
  public double ScaleForMonitor(string monitor){double value;return !string.IsNullOrEmpty(monitor)&&MonitorScales.TryGetValue(monitor,out value)?value:DefaultUiScale;}
  public void RememberScale(string monitor,double scale){if(!string.IsNullOrEmpty(monitor)&&scale>=1&&scale<=2)MonitorScales[monitor]=scale;}
  public double Thickness(string monitor,bool vertical){double value;return Thicknesses.TryGetValue(monitor+(vertical?"/vertical":"/horizontal"),out value)?value:(vertical?320:286);}
  public void RememberThickness(string monitor,bool vertical,double value){if(!double.IsNaN(value)&&!double.IsInfinity(value)&&value>0)Thicknesses[monitor+(vertical?"/vertical":"/horizontal")]=Math.Max(vertical?320:210,value);}
  public Dictionary<string,double> Clicks=new Dictionary<string,double>();
  public string ContextId="codex-strip-"+Guid.NewGuid().ToString("N");
  public static string DataDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
  public static string ImportWallpaper(string path){
   string source=Path.GetFullPath(path),root=Path.GetFullPath(Path.Combine(DataDir,"wallpapers"));
   if(!File.Exists(source))throw new FileNotFoundException("Wallpaper was not found",source);
   if(source.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return source;
   byte[] bytes=File.ReadAllBytes(source);string hash;using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
   string folder=Path.Combine(root,hash),target=Path.Combine(folder,Path.GetFileName(source));Directory.CreateDirectory(folder);
   if(!File.Exists(target))File.WriteAllBytes(target,bytes);return target;
  }
  public static string ResolveWallpaperPath(string path){
   if(string.IsNullOrWhiteSpace(path))return "";
   try{
    string root=Path.GetFullPath(DataDir)+Path.DirectorySeparatorChar;
    if(!Path.IsPathRooted(path)){string relative=Path.GetFullPath(Path.Combine(root,path));return relative.StartsWith(root,StringComparison.OrdinalIgnoreCase)?relative:"";}
    string absolute=Path.GetFullPath(path),marker=Path.DirectorySeparatorChar+"wallpapers"+Path.DirectorySeparatorChar;int start=absolute.LastIndexOf(marker,StringComparison.OrdinalIgnoreCase);
    if(start>=0){string tail=absolute.Substring(start+1);var parts=tail.Split(Path.DirectorySeparatorChar);if(parts.Length==3&&parts[1].Length==64&&parts[1].All(Uri.IsHexDigit)){string current=Path.GetFullPath(Path.Combine(root,tail));if(File.Exists(current))return current;}}
    return absolute;
   }catch{return "";}
  }
  string SavedWallpaperPath(){try{if(string.IsNullOrWhiteSpace(SleepWallpaperPath))return "";string root=Path.GetFullPath(DataDir)+Path.DirectorySeparatorChar,path=Path.GetFullPath(SleepWallpaperPath);return path.StartsWith(root,StringComparison.OrdinalIgnoreCase)?path.Substring(root.Length):SleepWallpaperPath;}catch{return SleepWallpaperPath;}}
  public bool CacheWallpaper(){string source=ResolveWallpaperPath(SleepWallpaperPath);if(string.IsNullOrWhiteSpace(source)||!File.Exists(source))return false;string path=ImportWallpaper(source);if(string.Equals(path,SleepWallpaperPath,StringComparison.OrdinalIgnoreCase))return false;SleepWallpaperPath=path;return true;}
  public static Settings Load(){try {var d=J.Parse(File.ReadAllText(Path.Combine(DataDir,"settings.json")));var s=new Settings();string mode=J.Str(d,"theme");if(mode=="light"||mode=="dark")s.ThemeMode=mode;double uiScale=J.Num(d,"uiScale");if(uiScale>=1&&uiScale<=2)s.UiScale=s.DefaultUiScale=uiScale;var monitorScales=J.Get(d,"monitorScales") as Dictionary<string,object>;if(monitorScales!=null)foreach(var p in monitorScales){double value;if(!string.IsNullOrEmpty(p.Key)&&double.TryParse(Convert.ToString(p.Value),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)&&value>=1&&value<=2)s.MonitorScales[p.Key]=value;}var pendingCompletions=J.Get(d,"pendingCompletions") as Dictionary<string,object>;if(pendingCompletions!=null)foreach(var p in pendingCompletions)if(!string.IsNullOrEmpty(p.Key))s.PendingCompletions[p.Key]=Convert.ToString(p.Value);s.Count=Math.Max(1,Math.Min(12,(int)J.Num(d,"count")));s.Topmost=!object.Equals(J.Get(d,"topmost"),false);string topMode=J.Str(d,"topmostMode");s.TopmostMode=new[]{"none","window","always"}.Contains(topMode)?topMode:(s.Topmost?"always":"none");s.Topmost=s.TopmostMode!="none";s.ReserveWorkArea=!object.Equals(J.Get(d,"reserveWorkArea"),false);string edge=J.Str(d,"dockEdge");if(new[]{"top","bottom","left","right"}.Contains(edge))s.DockEdge=edge;s.DockMonitor=J.Str(d,"dockMonitor");if(J.Num(d,"dockHorizontalSize")>0)s.DockHorizontalSize=Math.Max(210,J.Num(d,"dockHorizontalSize"));if(J.Num(d,"dockVerticalSize")>0)s.DockVerticalSize=Math.Max(320,J.Num(d,"dockVerticalSize"));s.Host=J.Str(d,"host");if(s.Host.Length==0)s.Host="all";s.Width=Math.Max(320,J.Num(d,"width"));s.Height=Math.Max(190,J.Num(d,"height"));s.Left=J.Num(d,"left");s.Top=J.Num(d,"top");var watched=J.Get(d,"watched") as Dictionary<string,object>;if(watched!=null)foreach(var p in watched)s.Watched[p.Key]=Convert.ToString(p.Value);var clicks=J.Get(d,"clicks") as Dictionary<string,object>;if(clicks!=null)foreach(var p in clicks)s.Clicks[p.Key]=Convert.ToDouble(p.Value);var sizes=J.Get(d,"thicknesses") as Dictionary<string,object>;if(sizes!=null)foreach(var p in sizes){double v;if(double.TryParse(Convert.ToString(p.Value),out v)&&v>0&&!double.IsInfinity(v))s.Thicknesses[p.Key]=v;}var rects=J.Get(d,"floatingRects") as Dictionary<string,object>;if(rects!=null)foreach(var p in rects){try{var a=J.Arr(p.Value).Select(Convert.ToDouble).ToArray();if(a.Length==4&&a.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v))&&a[2]>0&&a[3]>0)s.FloatingRects[p.Key]=a;}catch{}}LoadSleep(d,s);return s;}catch{return new Settings();}}
  static void LoadSleep(object data,Settings s){
   if(J.Get(data,"sleepAuto")!=null)s.SleepAuto=!object.Equals(J.Get(data,"sleepAuto"),false);
   if(J.Get(data,"sleepRingCharts")!=null)s.SleepRingCharts=!object.Equals(J.Get(data,"sleepRingCharts"),false);
   if(J.Get(data,"sleepSmoothRings")!=null)s.SleepSmoothRings=!object.Equals(J.Get(data,"sleepSmoothRings"),false);
   s.SleepDebug=object.Equals(J.Get(data,"sleepDebug"),true);s.SleepIgnoreKeywords=SleepDiagnostic.ParseKeywords(string.Join(",",J.Arr(J.Get(data,"sleepIgnoreKeywords")).Select(Convert.ToString)));s.SleepGlassRefraction=object.Equals(J.Get(data,"sleepGlassRefraction"),true);
   double minutes=J.Num(data,"sleepIdleMinutes");if(minutes>=1&&minutes<=60)s.SleepIdleMinutes=(int)minutes;
   var widgets=J.Arr(J.Get(data,"sleepWidgets")).Select(Convert.ToString).Where(SleepWidgetIds.Contains).Distinct().ToList();if(widgets.Count>0)s.SleepWidgets=widgets;
   s.SleepWallpaperPath=ResolveWallpaperPath(J.Str(data,"sleepWallpaperPath"));s.SleepAdapterId=J.Str(data,"sleepAdapterId");
   string unit=J.Str(data,"sleepSpeedUnit");if(new[]{"auto","mb","mbps"}.Contains(unit))s.SleepSpeedUnit=unit;
   string accent=J.Str(data,"sleepAccent");if(new[]{"blue","green","orange","purple"}.Contains(accent))s.SleepAccent=accent;
   double dim=J.Num(data,"sleepDim");if(dim>=.15&&dim<=.85)s.SleepDim=dim;
  }
  public void Save(){Directory.CreateDirectory(DataDir);var path=Path.Combine(DataDir,"settings.json");var tmp=path+".tmp";File.WriteAllText(tmp,J.Json(J.Obj("floatingRects",FloatingRects,"thicknesses",Thicknesses,"monitorScales",MonitorScales,"pendingCompletions",PendingCompletions,"theme",ThemeMode,"uiScale",DefaultUiScale,"count",Count,"topmost",Topmost,"topmostMode",TopmostMode,"reserveWorkArea",ReserveWorkArea,"dockEdge",DockEdge,"dockMonitor",DockMonitor,"dockHorizontalSize",DockHorizontalSize,"dockVerticalSize",DockVerticalSize,"host",Host,"width",Width,"height",Height,"left",Left,"top",Top,"clicks",Clicks,"watched",Watched,"sleepDebug",SleepDebug,"sleepIgnoreKeywords",SleepIgnoreKeywords.ToArray(),"sleepAuto",SleepAuto,"sleepRingCharts",SleepRingCharts,"sleepSmoothRings",SleepSmoothRings,"sleepGlassRefraction",SleepGlassRefraction,"sleepIdleMinutes",SleepIdleMinutes,"sleepWidgets",SleepWidgets.ToArray(),"sleepWallpaperPath",SavedWallpaperPath(),"sleepAdapterId",SleepAdapterId,"sleepSpeedUnit",SleepSpeedUnit,"sleepAccent",SleepAccent,"sleepDim",SleepDim)));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
 }
 public enum UnreadSource { Persisted, List, CachedSummary, Stream, Authoritative }
 static class UnreadClock {
  static readonly object gate=new object();static double last;
  public static double Next(){lock(gate){last=Math.Max(J.Now,last+.000001);return last;}}
  public static void Observe(double when){lock(gate)last=Math.Max(last,when);}
 }
 public sealed class Card {
  public bool Ephemeral; public string ParentId="",ParentHost=""; public string[] ChildIds=new string[0];
  public void NavigationMetadata(object state){
   if(object.Equals(J.Get(state,"ephemeral"),true)||object.Equals(J.Get(state,"sideConversation"),true))Ephemeral=true;
   string parent=J.Str(state,"parentThreadId");if(parent.Length==0)parent=J.Str(J.Get(J.Get(J.Get(state,"source"),"subAgent"),"thread_spawn"),"parent_thread_id");
   if(parent.Length>0){ParentId=parent;ParentHost=Host;}
   string path=J.Str(state,"sideConversationParentNavigationPath");
   if(path.Length>0){Uri route;if(Uri.TryCreate("https://codex.invalid"+path,UriKind.Absolute,out route)&&route.Host=="codex.invalid"){
    var parts=route.AbsolutePath.Split('/');Guid id;if(parts.Length==3&&parts[1]=="local"&&Guid.TryParseExact(parts[2],"D",out id)){ParentId=id.ToString("D");ParentHost=Host;foreach(string part in route.Query.TrimStart('?').Split('&')){var pair=part.Split(new[]{'='},2);if(pair.Length==2&&pair[0]=="hostId")ParentHost=Uri.UnescapeDataString(pair[1]);}}
   }}
  }
  public void ChildMetadata(IEnumerable<object> items){ChildIds=ChildIds.Concat(items.SelectMany(item=>{
   string type=J.Str(item,"type");if(type=="subAgentActivity")return new[]{J.Str(item,"agentThreadId")};
   if(type=="collabAgentToolCall"&&J.Str(item,"tool")=="spawnAgent")return J.Arr(J.Get(item,"receiverThreadIds")).Select(Convert.ToString);
   return new string[0];
  })).Where(id=>id.Length>0).Distinct().ToArray();}
  public string Id,Host,Title,Status="unknown",Message="",MessageKind="",TurnId="",LastError="",Cursor="";
  public double Interaction,Started,LastSeen,DetailFetched,UnreadUpdated; public bool Stale,QuestionPending,MessageUnavailable,Live,Unread,UnreadKnown,CompletionPending;
  public UnreadSource ReadSource;public string UnreadTurnId="",UnreadStatus="";public bool ReadConfirmed;public double ReadConfirmedAt;
  public bool UnreadResult {get{return Status=="completed"&&(Unread||CompletionPending)&&!Stale&&!QuestionPending;}}
  public void SetUnread(bool unread,double observedAt){SetUnread(unread,observedAt,UnreadSource.Authoritative,TurnId);}
  public void SetUnread(bool unread,double observedAt,UnreadSource source,string turnId){SetUnread(unread,observedAt,source,turnId,Status);}
  void SetUnread(bool unread,double observedAt,UnreadSource source,string turnId,string status){
   turnId=turnId??"";if(turnId.Length==0&&(int)source<(int)UnreadSource.Stream)turnId=TurnId;
   status=string.IsNullOrEmpty(status)||status=="unknown"||status=="idle"?Status:status;
   // A receipt belongs to the turn it acknowledged. It cannot acknowledge a later turn.
   if(turnId.Length>0&&TurnId.Length>0&&turnId!=TurnId)return;
   bool sameTurn=UnreadTurnId.Length==0||turnId.Length==0||UnreadTurnId==turnId;
   bool confirmed=!unread&&(int)source>=(int)UnreadSource.CachedSummary&&status=="completed"&&turnId.Length>0;
   if(UnreadKnown&&sameTurn){
    // Unversioned summaries cannot undo a viewed result. A later explicit
    // receipt can still mark that same result unread from the desktop menu.
    if(unread&&ReadConfirmed&&(source!=UnreadSource.Authoritative||observedAt<=ReadConfirmedAt))return;
    // Replaying the cached false that preceded a manual unread receipt is stale.
    if(!unread&&Unread&&ReadSource==UnreadSource.Authoritative&&source!=UnreadSource.Authoritative&&observedAt<=UnreadUpdated)return;
    if(confirmed&&ReadConfirmed&&observedAt<=ReadConfirmedAt)return;
    if(!confirmed&&((int)source<(int)ReadSource||(source==ReadSource&&observedAt<UnreadUpdated)))return;
    if(source==ReadSource&&observedAt==UnreadUpdated&&unread==Unread&&!confirmed)return;
   }
   Unread=unread;UnreadKnown=true;UnreadUpdated=observedAt;ReadSource=source;UnreadTurnId=turnId;UnreadStatus=status;ReadConfirmed=confirmed;ReadConfirmedAt=confirmed?observedAt:0;
  }
  public void MergeUnread(Card other){if(other!=null&&other.UnreadKnown)SetUnread(other.Unread,other.UnreadUpdated,other.ReadSource,other.UnreadTurnId,other.UnreadStatus);}
  public Card Copy(){return (Card)MemberwiseClone();}
  public string Key {get{return Host+"/"+Id;}}
  public string HostLabel {get{return Host=="local"?"LOCAL":Host.Replace("remote-ssh-discovered:","").ToUpperInvariant();}}
  public static double Rank(Card c,Settings s){double clicked;s.Clicks.TryGetValue(c.Key,out clicked);return Math.Max(c.Interaction,clicked);}
  public static Card[] Sort(IEnumerable<Card> cards,Settings s){return cards.Where(c=>s.Host=="all"||c.Host==s.Host).OrderByDescending(c=>Rank(c,s)).ThenBy(c=>c.Key,StringComparer.Ordinal).Take(s.Count).ToArray();}
  public void Snapshot(object poll){
   if(J.Get(poll,"thread")==null)return;
   NavigationMetadata(J.Get(poll,"thread"));
   string cursor=J.Str(poll,"cursor");if(cursor.Length>0)Cursor=cursor;
   var state=J.Get(J.Get(poll,"thread"),"status");var flags=J.Arr(J.Get(state,"activeFlags")).Select(Convert.ToString).ToArray();Status=J.Str(state,"type");
   if(flags.Contains("waitingOnApproval"))Status="approval";else if(flags.Contains("waitingOnUserInput"))Status="input";else if(flags.Contains("reconnecting"))Status="reconnecting";
   var turn=J.Get(poll,"latestTurn");if(turn!=null){string tid=J.Str(turn,"id");double started=J.Num(turn,"startedAt");if(started>0){if(TurnId.Length==0)Interaction=started;else Interaction=Math.Max(Interaction,started);Started=started;}if(TurnId!=tid){DetailFetched=0;if(UnreadKnown&&UnreadTurnId.Length>0&&UnreadTurnId!=tid){Unread=false;UnreadKnown=false;UnreadUpdated=0;UnreadTurnId="";UnreadStatus="";ReadSource=UnreadSource.Persisted;ReadConfirmed=false;ReadConfirmedAt=0;}TurnId=tid;}string ts=J.Str(turn,"status");if(ts=="failed")Status="failed";else if(ts=="interrupted")Status="interrupted";else if(ts=="completed"&&(Status=="idle"||Status=="notLoaded"))Status="completed";LastError=J.Str(J.Get(turn,"error"),"message");}
   var msg=J.Get(poll,"latestAssistantMessage");string text=msg as string;if(text==null)text=J.Str(msg,"text");if(!string.IsNullOrWhiteSpace(text)){Message=text;MessageKind="进展";}
   LastSeen=J.Now;Stale=false;
  }
  public void Detail(object result){NavigationMetadata(J.Get(result,"thread"));var turns=J.Arr(J.Get(result,"turns"));ChildMetadata(turns.SelectMany(t=>J.Arr(J.Get(t,"items"))));if(turns.Length==0){MessageUnavailable=true;DetailFetched=J.Now;return;}var turn=turns[0];Snapshot(J.Obj("thread",J.Get(result,"thread"),"latestTurn",turn));double started=J.Num(turn,"startedAt");if(started>0){Interaction=Math.Max(Interaction,started);Started=started;}
   // User message IDs in current desktop builds are UUIDv7. Their embedded time
   // lets steering messages count as interaction; other formats use turn start.
   foreach(var item in J.Arr(J.Get(turn,"items"))){if(J.Str(item,"type")!="userMessage")continue;string id=J.Str(item,"id");if(id.Length==36&&id[14]=='7'){long ms;if(long.TryParse(id.Substring(0,8)+id.Substring(9,4),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out ms)){double when=ms/1000.0;if(when>started-60&&when<J.Now+300)Interaction=Math.Max(Interaction,when);}}}
   var messages=turns.SelectMany(t=>J.Arr(J.Get(t,"items")).Where(i=>J.Str(i,"type")=="agentMessage"&&!string.IsNullOrWhiteSpace(J.Str(i,"text"))).Reverse()).ToArray();MessageUnavailable=messages.Length==0;if(messages.Length>0){var m=messages.First();Message=J.Str(m,"text");MessageKind=J.Str(m,"phase")=="final_answer"?"最新回复":"最新进展";}
   if(Message.Length>4000)Message=Message.Substring(0,4000)+"…";DetailFetched=J.Now;
  }
 }
 public static class ThreadNavigation {
  public static Card Resolve(Card card,IEnumerable<Card> cards){
   if(card==null)throw new ArgumentNullException("card");var known=cards.ToDictionary(c=>c.Key);var seen=new HashSet<string>();var current=card;
   for(int depth=0;depth<32;depth++){
    if(!seen.Add(current.Key))throw new IOException(UiText.T("线程的主线程关联形成循环，无法跳转。","The parent conversation links form a cycle."));
    string parent=current.ParentId,host=current.ParentHost.Length>0?current.ParentHost:current.Host;
    if(parent.Length==0){var owners=known.Values.Where(c=>c.Host==current.Host&&c.ChildIds.Contains(current.Id)).ToArray();if(owners.Length==1){parent=owners[0].Id;host=owners[0].Host;}}
    if(parent.Length==0){if(current.Ephemeral)throw new IOException(UiText.T("该临时线程的主线程信息暂不可用，请从 Codex 侧栏打开主线程。","Open the parent conversation from the Codex sidebar."));return current;}
    Guid id;if(!Guid.TryParseExact(parent,"D",out id)||string.IsNullOrWhiteSpace(host)||host.Any(char.IsControl))throw new ArgumentException(UiText.T("主线程关联信息无效。","Invalid parent conversation link."));
    Card next;current=known.TryGetValue(host+"/"+parent,out next)?next:new Card{Id=parent,Host=host};
   }
   throw new IOException(UiText.T("线程的主线程关联层级过深，无法跳转。","The parent conversation chain is too deep."));
  }
  public static bool UsesDesktopLink(Card card){return card!=null&&!string.IsNullOrEmpty(card.Host)&&card.Host!="local";}
  public static string DesktopLink(Card card){
   Guid id;
   if(card==null||!Guid.TryParseExact(card.Id,"D",out id)||string.IsNullOrWhiteSpace(card.Host)||card.Host.Any(char.IsControl))throw new ArgumentException(UiText.T("线程链接信息不完整。","Incomplete conversation link."));
   return "codex://threads/"+id.ToString("D")+"?hostId="+Uri.EscapeDataString(card.Host);
  }
  static void Launch(string link){using(var process=Process.Start(new ProcessStartInfo(link){UseShellExecute=true})){} }
  public static async Task Open(Card card,Func<string,object,int,Task<object>> call,Action<string> launch=null){
   if(card==null)throw new ArgumentNullException("card");
   // The desktop tool has no hostId argument. Its native links preserve the
   // cloud/SSH host instead of trying to restore the thread on the local host.
   if(UsesDesktopLink(card)){(launch??Launch)(DesktopLink(card));return;}
   await call("navigate_to_codex_page",J.Obj("threadId",card.Id),12000);
  }
 }
 public sealed class Credit {public string Title;public double Expires;}
 public sealed class Usage {
  public double? WeeklyRemaining;public double ResetsAt,Updated;public Credit[] Credits=new Credit[0];public int? Count;public string Error="";
  public void Update(object result){
   var by=J.Get(result,"rateLimitsByLimitId");var bucket=J.Get(by,"codex")??J.Get(result,"rateLimits");WeeklyRemaining=null;ResetsAt=0;
   foreach(string k in new[]{"primary","secondary"}){var w=J.Get(bucket,k);if(J.Num(w,"windowDurationMins")==10080&&J.Get(w,"usedPercent")!=null){WeeklyRemaining=Math.Max(0,Math.Min(100,100-J.Num(w,"usedPercent")));ResetsAt=J.Num(w,"resetsAt");}}
   var credits=J.Get(result,"rateLimitResetCredits");Count=J.Get(credits,"availableCount")==null?(int?)null:(int)J.Num(credits,"availableCount");Credits=J.Arr(J.Get(credits,"credits")).Where(c=>J.Str(c,"status")=="available").Select(c=>new Credit{Title=J.Str(c,"title"),Expires=J.Num(c,"expiresAt")}).OrderBy(c=>c.Expires<=0?double.MaxValue:c.Expires).ToArray();Updated=J.Now;Error="";
  }
 }
 public sealed class MonitorEngine {
  public readonly Bridge Bridge=new Bridge();public Dictionary<string,Card> Cards=new Dictionary<string,Card>();public readonly Usage Usage=new Usage();
  public string Connection="正在连接 Codex…",Unavailable="";public bool Connected,Refreshing;public DateTime LastRefresh;
  public Action Changed=delegate{};public Settings Settings; bool refreshingUsage;
  public DesktopStream Stream;
  readonly Dictionary<string,Card> listedReadState=new Dictionary<string,Card>();
  public void ApplyStream(){if(Stream==null)return;var next=Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());MergeStream(next);UpdateCompletions(Cards,next);Cards=next;Changed();}
  public static Card PreferFresh(Card previous,Card live){if(previous!=null&&((live.Stale&&!previous.Stale)||(previous.TurnId.Length>0&&live.TurnId.Length>0&&previous.TurnId!=live.TurnId&&previous.Started>live.Started)))return previous;if(previous!=null){live.Cursor=previous.Cursor;live.DetailFetched=previous.DetailFetched;live.MergeUnread(previous);live.Ephemeral|=previous.Ephemeral;if(live.ParentId.Length==0){live.ParentId=previous.ParentId;live.ParentHost=previous.ParentHost;}live.ChildIds=live.ChildIds.Concat(previous.ChildIds).Distinct().ToArray();}return live;}
  void MergeStream(Dictionary<string,Card> cards){if(Stream==null)return;Stream.Watch(cards.Values);foreach(var live in Stream.Latest()){Card previous;cards.TryGetValue(live.Key,out previous);if(previous==null&&live.Stale)continue;cards[live.Key]=PreferFresh(previous,live);}Stream.ApplyReadState(cards.Values);ApplyLocalAnswers(cards);}
  bool ApplyLocalAnswers(Dictionary<string,Card> cards){bool changed=false;foreach(var card in cards.Values.Where(c=>c.Host=="local")){string text;if(!localMessages.ClearAnswered(card,out text))continue;card.QuestionPending=false;if(card.Status=="input")card.Status="active";if(text.Length>0){card.Message=text;card.MessageKind="最新对话";}changed=true;}return changed;}
  public static bool NewCompletion(Card previous,Card current){return previous!=null&&!previous.Stale&&!current.Stale&&current.Status=="completed"&&new[]{"active","input","approval","reconnecting"}.Contains(previous.Status);}
  // Local completion markers are only a fallback while desktop read state is unknown.
  // An explicit desktop read/unread value owns the notification, including after restart.
  public static bool ReconcileCompletion(Settings settings,Card previous,Card current){
   bool changed=false;string turn;
   // A false observed while running does not acknowledge the result that follows.
   bool readCurrent=current.UnreadKnown&&!(current.Status=="completed"&&!current.Unread&&current.UnreadStatus!="completed");
   if(settings.PendingCompletions.TryGetValue(current.Key,out turn)&&(readCurrent||(turn.Length>0&&current.TurnId.Length>0&&turn!=current.TurnId))){settings.PendingCompletions.Remove(current.Key);changed=true;}
   if(!readCurrent&&NewCompletion(previous,current)){settings.PendingCompletions[current.Key]=current.TurnId;changed=true;}
   current.CompletionPending=settings.PendingCompletions.ContainsKey(current.Key);return changed;
  }
  void UpdateCompletions(Dictionary<string,Card> before,Dictionary<string,Card> after){bool changed=false;foreach(var c in after.Values){Card previous;before.TryGetValue(c.Key,out previous);changed=ReconcileCompletion(Settings,previous,c)||changed;}if(changed)try{Settings.Save();}catch{}}
  readonly LocalMessages localMessages=new LocalMessages(); bool readingLocal;
  readonly Func<string,object,int,Task<object>> fetch;
  public MonitorEngine(Settings s,Func<string,object,int,Task<object>> request=null){Settings=s;fetch=request;}
  Task<object> Call(string tool,object args,int timeout=12000){return fetch==null?Bridge.Call(tool,args,timeout):fetch(tool,args,timeout);}
  public async Task RefreshLocal(){if(readingLocal||Refreshing)return;readingLocal=true;try{
   var current=Cards;var shown=Card.Sort(current.Values,Settings).Where(c=>c.Host=="local").ToArray();var updates=new Dictionary<string,string>();
   foreach(var c in shown){var msg=await Task.Run(()=>localMessages.Read(c));if(!c.Live&&msg.Length>0&&(msg!=c.Message||c.QuestionPending!=localMessages.Waiting(c)))updates[c.Key]=msg;}
   // A full refresh may have started while the file reads were in flight.
   if(Refreshing||!object.ReferenceEquals(current,Cards))return;
   var next=current.ToDictionary(p=>p.Key,p=>p.Value.Copy());bool cleared=ApplyLocalAnswers(next);if(updates.Count==0&&!cleared)return;foreach(var p in updates){next[p.Key].Message=p.Value;next[p.Key].MessageKind="最新对话";next[p.Key].MessageUnavailable=false;next[p.Key].QuestionPending=localMessages.Waiting(next[p.Key]);}Cards=next;Changed();
  }finally{readingLocal=false;}}
  public async Task Refresh(){if(Refreshing)return;Refreshing=true;
   // Work only on detached cards; readers keep the last complete snapshot.
   var Cards=this.Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());bool Connected=this.Connected;string Connection=this.Connection,Unavailable=this.Unavailable;
   try {
   // Desktop host enumeration can take 30 seconds before reporting an offline host.
   double readObservedAt=UnreadClock.Next();object result=await Call("list_threads",J.Obj("limit",50),45000);var listedUnread=new Dictionary<string,bool>();
   var unavailableHosts=new HashSet<string>(J.Arr(J.Get(result,"unavailableHosts")).Select(h=>J.Str(h,"hostId")).Where(h=>h.Length>0));
   var list=J.Arr(J.Get(result,"pinnedThreads")).Concat(J.Arr(J.Get(result,"threads"))).Where(t=>J.Str(t,"kind")=="codex").ToArray();
   var keys=new HashSet<string>();foreach(var t in list){string id=J.Str(t,"id"),host=J.Str(t,"hostId");if(host.Length==0)host="local";string key=host+"/"+id;if(!keys.Add(key))continue;Card c;if(!Cards.TryGetValue(key,out c)){c=new Card{Id=id,Host=host,Interaction=J.Num(t,"updatedAt")};Cards[key]=c;}c.Title=J.Str(t,"title");c.Status=J.Str(t,"status");if(J.Get(t,"isUnread") is bool)listedUnread[key]=(bool)J.Get(t,"isUnread");}
   foreach(var t in list){string host=J.Str(t,"hostId");if(host.Length==0)host="local";Cards[host+"/"+J.Str(t,"id")].NavigationMetadata(t);}
   // Explicitly tracked links remain discoverable even when the app list omits them.
   foreach(var watch in Settings.Watched){string key=watch.Value+"/"+watch.Key;if(keys.Contains(key))continue;Card c;if(!Cards.TryGetValue(key,out c))c=new Card{Id=watch.Key,Host=watch.Value,Title=UiText.T("正在读取线程","Loading conversation")};if(unavailableHosts.Contains(c.Host)){Cards[key]=c;keys.Add(key);c.Stale=true;continue;}try{var detail=await Call("read_thread",J.Obj("threadId",c.Id,"hostId",c.Host,"turnLimit",2,"includeOutputs",false,"maxOutputCharsPerItem",1200),15000);c.Title=J.Str(J.Get(detail,"thread"),"title");c.Detail(detail);Cards[key]=c;keys.Add(key);}catch{if(Cards.ContainsKey(key)){keys.Add(key);c.Stale=true;}}}
   Unavailable=J.Arr(J.Get(result,"unavailableHosts")).Length>0?"部分主机暂不可用":"";
   // Preserve last known remote cards when a host is disconnected.
   foreach(var k in Cards.Keys.ToArray())if(!keys.Contains(k)&&!Cards[k].Live){if(Unavailable.Length>0)Cards[k].Stale=true;else Cards.Remove(k);}
   foreach(var c in Cards.Values)if(unavailableHosts.Contains(c.Host))c.Stale=true;
   Connected=true;Connection="已连接 Codex";
   // wait_threads must not wait on its own routing task. Read that one directly.
   var targets=Cards.Values.Where(c=>keys.Contains(c.Key)&&c.Id!=Bridge.ContextId&&!unavailableHosts.Contains(c.Host)).ToArray();
   for(int start=0;start<targets.Length;start+=8){var group=targets.Skip(start).Take(8).ToArray();try {
    var snapshot=await Call("wait_threads",J.Obj("targets",group.Select(c=>Target(c)).ToArray(),"timeoutMs",0),15000);
    var seen=new HashSet<string>();foreach(var p in J.Arr(J.Get(snapshot,"polls"))){var th=J.Get(p,"thread");string key=J.Str(th,"hostId")+"/"+J.Str(th,"id");Card c;if(Cards.TryGetValue(key,out c)){c.Snapshot(p);seen.Add(key);}}
    // A completed target can wake the batch before other snapshots arrive.
    // Absence is not a disconnect: fetch missing targets individually.
    await Task.WhenAll(group.Where(c=>!seen.Contains(c.Key)).Select(async c=>{try{var single=await Call("wait_threads",J.Obj("targets",new[]{Target(c)},"timeoutMs",0),15000);var polls=J.Arr(J.Get(single,"polls"));if(polls.Length>0)c.Snapshot(polls[0]);else c.Stale=true;}catch{c.Stale=true;}}));
   }catch{foreach(var c in group)c.Stale=true;}}
   foreach(var c in Card.Sort(Cards.Values,Settings).Concat(Cards.Values.Where(c=>c.Status=="active"||c.Id==Bridge.ContextId)).Distinct()){if(unavailableHosts.Contains(c.Host)||J.Now-c.DetailFetched<5&&c.Message.Length>0)continue;try {c.Detail(await Call("read_thread",J.Obj("threadId",c.Id,"hostId",c.Host,"turnLimit",2,"includeOutputs",false,"maxOutputCharsPerItem",1200),15000));}catch{c.DetailFetched=J.Now;}}
   foreach(var c in Card.Sort(Cards.Values,Settings).Where(c=>c.Host=="local"&&!c.Live)){var msg=await Task.Run(()=>localMessages.Read(c));if(msg.Length>0){c.Message=msg;c.MessageKind="最新对话";c.MessageUnavailable=false;c.QuestionPending=localMessages.Waiting(c);}}
   // Bind a successful list observation after polling has identified its current turn.
   foreach(var entry in listedUnread){Card c;if(!Cards.TryGetValue(entry.Key,out c))continue;Card observation;
    if(!listedReadState.TryGetValue(entry.Key,out observation)||observation.TurnId!=c.TurnId||observation.Unread!=entry.Value){observation=new Card{Host=c.Host,Id=c.Id,TurnId=c.TurnId,Status=c.Status};observation.SetUnread(entry.Value,readObservedAt,UnreadSource.List,c.TurnId);listedReadState[entry.Key]=observation;}
    c.MergeUnread(observation);
   }
   LastRefresh=DateTime.Now;Connection="已连接 Codex";if(Unavailable.Length>0)Connection=Unavailable;
  }catch(TimeoutException){Connected=this.Connected||(Stream!=null&&Stream.Connected);Connection="任务列表超时 · 正在重试";Cards=this.Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());foreach(var c in Cards.Values)c.Stale=true;
  }catch(Exception){Connected=false;Connection="Codex 未连接 · 等待重连";Cards=this.Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());foreach(var c in Cards.Values)c.Stale=true;Bridge.Reset();}finally{MergeStream(Cards);UpdateCompletions(this.Cards,Cards);this.Cards=Cards;this.Connected=Connected;this.Connection=Connection;this.Unavailable=Unavailable;Refreshing=false;Changed();}}
  public async Task RefreshUsage(){if(refreshingUsage)return;refreshingUsage=true;try{Usage.Update(await Call("get_usage_limits",J.Obj(),15000));}catch{Usage.Error="额度暂不可读取";}finally{refreshingUsage=false;Changed();}}
  public async Task Open(Card card){var target=ThreadNavigation.Resolve(card,Cards.Values);var window=CodexForeground.Find();bool activated=CodexForeground.Activate(window);await ThreadNavigation.Open(target,Call);if(!activated&&!CodexForeground.Activate(window))throw new IOException(UiText.T("已请求切换线程，但 Windows 未允许激活 Codex 窗口。","Conversation opened; activate the Codex window from the taskbar."));Settings.Clicks[card.Key]=J.Now;
   // Shell acceptance cannot confirm that a hosted conversation loaded. Its
   // desktop read receipt will clear completion markers during the next poll.
   if(target.Key==card.Key&&!ThreadNavigation.UsesDesktopLink(target)){Settings.PendingCompletions.Remove(card.Key);Card shown;if(Cards.TryGetValue(card.Key,out shown))shown.CompletionPending=false;}
   Settings.Save();Changed();}
  static object Target(Card c){return c.Cursor.Length>0?J.Obj("threadId",c.Id,"hostId",c.Host,"afterCursor",c.Cursor):J.Obj("threadId",c.Id,"hostId",c.Host);}
 }
}







