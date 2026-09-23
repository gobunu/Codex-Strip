using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodexStrip {
 public sealed class Settings {
  public string ThemeMode="system"; public int Count=4; public bool Topmost=true; public string TopmostMode="always"; public bool ReserveWorkArea=true; public string DockEdge="top"; public string DockMonitor=""; public double DockHorizontalSize=286,DockVerticalSize=320; public string Host="all"; public double Width=1280,Height=286,Left=80,Top=80;
  public Dictionary<string,string> Watched=new Dictionary<string,string>();
  public Dictionary<string,double[]> FloatingRects=new Dictionary<string,double[]>();
  public Dictionary<string,double> Thicknesses=new Dictionary<string,double>();
  public double Thickness(string monitor,bool vertical){double value;return Thicknesses.TryGetValue(monitor+(vertical?"/vertical":"/horizontal"),out value)?value:(vertical?320:286);}
  public void RememberThickness(string monitor,bool vertical,double value){if(!double.IsNaN(value)&&!double.IsInfinity(value)&&value>0)Thicknesses[monitor+(vertical?"/vertical":"/horizontal")]=Math.Max(vertical?320:210,value);}
  public Dictionary<string,double> Clicks=new Dictionary<string,double>();
  public string ContextId="codex-strip-"+Guid.NewGuid().ToString("N");
  public static string DataDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
  public static Settings Load(){try {var d=J.Parse(File.ReadAllText(Path.Combine(DataDir,"settings.json")));var s=new Settings();string mode=J.Str(d,"theme");if(mode=="light"||mode=="dark")s.ThemeMode=mode;s.Count=Math.Max(1,Math.Min(12,(int)J.Num(d,"count")));s.Topmost=!object.Equals(J.Get(d,"topmost"),false);string topMode=J.Str(d,"topmostMode");s.TopmostMode=new[]{"none","window","always"}.Contains(topMode)?topMode:(s.Topmost?"always":"none");s.Topmost=s.TopmostMode!="none";s.ReserveWorkArea=!object.Equals(J.Get(d,"reserveWorkArea"),false);string edge=J.Str(d,"dockEdge");if(new[]{"top","bottom","left","right"}.Contains(edge))s.DockEdge=edge;s.DockMonitor=J.Str(d,"dockMonitor");if(J.Num(d,"dockHorizontalSize")>0)s.DockHorizontalSize=Math.Max(210,J.Num(d,"dockHorizontalSize"));if(J.Num(d,"dockVerticalSize")>0)s.DockVerticalSize=Math.Max(320,J.Num(d,"dockVerticalSize"));s.Host=J.Str(d,"host");if(s.Host.Length==0)s.Host="all";s.Width=Math.Max(320,J.Num(d,"width"));s.Height=Math.Max(190,J.Num(d,"height"));s.Left=J.Num(d,"left");s.Top=J.Num(d,"top");var watched=J.Get(d,"watched") as Dictionary<string,object>;if(watched!=null)foreach(var p in watched)s.Watched[p.Key]=Convert.ToString(p.Value);var clicks=J.Get(d,"clicks") as Dictionary<string,object>;if(clicks!=null)foreach(var p in clicks)s.Clicks[p.Key]=Convert.ToDouble(p.Value);var sizes=J.Get(d,"thicknesses") as Dictionary<string,object>;if(sizes!=null)foreach(var p in sizes){double v;if(double.TryParse(Convert.ToString(p.Value),out v)&&v>0&&!double.IsInfinity(v))s.Thicknesses[p.Key]=v;}var rects=J.Get(d,"floatingRects") as Dictionary<string,object>;if(rects!=null)foreach(var p in rects){try{var a=J.Arr(p.Value).Select(Convert.ToDouble).ToArray();if(a.Length==4&&a.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v))&&a[2]>0&&a[3]>0)s.FloatingRects[p.Key]=a;}catch{}}return s;}catch{return new Settings();}}
  public void Save(){Directory.CreateDirectory(DataDir);var path=Path.Combine(DataDir,"settings.json");var tmp=path+".tmp";File.WriteAllText(tmp,J.Json(J.Obj("floatingRects",FloatingRects,"thicknesses",Thicknesses,"theme",ThemeMode,"count",Count,"topmost",Topmost,"topmostMode",TopmostMode,"reserveWorkArea",ReserveWorkArea,"dockEdge",DockEdge,"dockMonitor",DockMonitor,"dockHorizontalSize",DockHorizontalSize,"dockVerticalSize",DockVerticalSize,"host",Host,"width",Width,"height",Height,"left",Left,"top",Top,"clicks",Clicks,"watched",Watched)));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
 }
 public sealed class Card {
  public string Id,Host,Title,Status="unknown",Message="",MessageKind="",TurnId="",LastError="",Cursor="";
  public double Interaction,Started,LastSeen,DetailFetched; public bool Stale,QuestionPending,MessageUnavailable,Live,Unread;
  public bool UnreadResult {get{return Status=="completed"&&Unread&&!Stale&&!QuestionPending;}}
  public Card Copy(){return (Card)MemberwiseClone();}
  public string Key {get{return Host+"/"+Id;}}
  public string HostLabel {get{return Host=="local"?"LOCAL":Host.Replace("remote-ssh-discovered:","").ToUpperInvariant();}}
  public static double Rank(Card c,Settings s){double clicked;s.Clicks.TryGetValue(c.Key,out clicked);return Math.Max(c.Interaction,clicked);}
  public static Card[] Sort(IEnumerable<Card> cards,Settings s){return cards.Where(c=>s.Host=="all"||c.Host==s.Host).OrderByDescending(c=>Rank(c,s)).ThenBy(c=>c.Key,StringComparer.Ordinal).Take(s.Count).ToArray();}
  public void Snapshot(object poll){
   if(J.Get(poll,"thread")==null)return;
   string cursor=J.Str(poll,"cursor");if(cursor.Length>0)Cursor=cursor;
   var state=J.Get(J.Get(poll,"thread"),"status");var flags=J.Arr(J.Get(state,"activeFlags")).Select(Convert.ToString).ToArray();Status=J.Str(state,"type");
   if(flags.Contains("waitingOnApproval"))Status="approval";else if(flags.Contains("waitingOnUserInput"))Status="input";else if(flags.Contains("reconnecting"))Status="reconnecting";
   var turn=J.Get(poll,"latestTurn");if(turn!=null){string tid=J.Str(turn,"id");double started=J.Num(turn,"startedAt");if(started>0){if(TurnId.Length==0)Interaction=started;else Interaction=Math.Max(Interaction,started);Started=started;}if(TurnId!=tid){DetailFetched=0;TurnId=tid;}string ts=J.Str(turn,"status");if(ts=="failed")Status="failed";else if(ts=="interrupted")Status="interrupted";else if(ts=="completed"&&(Status=="idle"||Status=="notLoaded"))Status="completed";LastError=J.Str(J.Get(turn,"error"),"message");}
   var msg=J.Get(poll,"latestAssistantMessage");string text=msg as string;if(text==null)text=J.Str(msg,"text");if(!string.IsNullOrWhiteSpace(text)){Message=text;MessageKind="进展";}
   LastSeen=J.Now;Stale=false;
  }
  public void Detail(object result){var turns=J.Arr(J.Get(result,"turns"));if(turns.Length==0){MessageUnavailable=true;DetailFetched=J.Now;return;}var turn=turns[0];Snapshot(J.Obj("thread",J.Get(result,"thread"),"latestTurn",turn));double started=J.Num(turn,"startedAt");if(started>0){Interaction=Math.Max(Interaction,started);Started=started;}
   // User message IDs in current desktop builds are UUIDv7. Their embedded time
   // lets steering messages count as interaction; other formats use turn start.
   foreach(var item in J.Arr(J.Get(turn,"items"))){if(J.Str(item,"type")!="userMessage")continue;string id=J.Str(item,"id");if(id.Length==36&&id[14]=='7'){long ms;if(long.TryParse(id.Substring(0,8)+id.Substring(9,4),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out ms)){double when=ms/1000.0;if(when>started-60&&when<J.Now+300)Interaction=Math.Max(Interaction,when);}}}
   var messages=turns.SelectMany(t=>J.Arr(J.Get(t,"items")).Where(i=>J.Str(i,"type")=="agentMessage"&&!string.IsNullOrWhiteSpace(J.Str(i,"text"))).Reverse()).ToArray();MessageUnavailable=messages.Length==0;if(messages.Length>0){var m=messages.First();Message=J.Str(m,"text");MessageKind=J.Str(m,"phase")=="final_answer"?"最新回复":"最新进展";}
   if(Message.Length>4000)Message=Message.Substring(0,4000)+"…";DetailFetched=J.Now;
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
  public void ApplyStream(){if(Stream==null)return;var next=Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());MergeStream(next);Cards=next;Changed();}
  void MergeStream(Dictionary<string,Card> cards){if(Stream==null)return;foreach(var live in Stream.Latest()){Card previous;if(cards.TryGetValue(live.Key,out previous)){live.Cursor=previous.Cursor;live.DetailFetched=previous.DetailFetched;}cards[live.Key]=live;}}
  readonly LocalMessages localMessages=new LocalMessages(); bool readingLocal;
  readonly Func<string,object,int,Task<object>> fetch;
  public MonitorEngine(Settings s,Func<string,object,int,Task<object>> request=null){Settings=s;fetch=request;}
  Task<object> Call(string tool,object args,int timeout=12000){return fetch==null?Bridge.Call(tool,args,timeout):fetch(tool,args,timeout);}
  public async Task RefreshLocal(){if(readingLocal||Refreshing)return;readingLocal=true;try{
   var current=Cards;var shown=Card.Sort(current.Values,Settings).Where(c=>c.Host=="local"&&!c.Live).ToArray();var updates=new Dictionary<string,string>();
   foreach(var c in shown){var msg=await Task.Run(()=>localMessages.Read(c));if(msg.Length>0&&(msg!=c.Message||c.QuestionPending!=localMessages.Waiting(c)))updates[c.Key]=msg;}
   // A full refresh may have started while the file reads were in flight.
   if(Refreshing||!object.ReferenceEquals(current,Cards)||updates.Count==0)return;
   var next=current.ToDictionary(p=>p.Key,p=>p.Value.Copy());foreach(var p in updates){next[p.Key].Message=p.Value;next[p.Key].MessageKind="最新对话";next[p.Key].MessageUnavailable=false;next[p.Key].QuestionPending=localMessages.Waiting(next[p.Key]);}Cards=next;Changed();
  }finally{readingLocal=false;}}
  public async Task Refresh(){if(Refreshing)return;Refreshing=true;
   // Work only on detached cards; readers keep the last complete snapshot.
   var Cards=this.Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());bool Connected=this.Connected;string Connection=this.Connection,Unavailable=this.Unavailable;
   try {
   object result=await Call("list_threads",J.Obj("limit",50));
   var list=J.Arr(J.Get(result,"pinnedThreads")).Concat(J.Arr(J.Get(result,"threads"))).Where(t=>J.Str(t,"kind")=="codex").ToArray();
   var keys=new HashSet<string>();foreach(var t in list){string id=J.Str(t,"id"),host=J.Str(t,"hostId");if(host.Length==0)host="local";string key=host+"/"+id;if(!keys.Add(key))continue;Card c;if(!Cards.TryGetValue(key,out c)){c=new Card{Id=id,Host=host,Interaction=J.Num(t,"updatedAt")};Cards[key]=c;}c.Title=J.Str(t,"title");c.Status=J.Str(t,"status");}
   // Explicitly tracked links remain discoverable even when the app list omits them.
   foreach(var watch in Settings.Watched){string key=watch.Value+"/"+watch.Key;if(keys.Contains(key))continue;Card c;if(!Cards.TryGetValue(key,out c))c=new Card{Id=watch.Key,Host=watch.Value,Title="正在读取线程"};try{var detail=await Call("read_thread",J.Obj("threadId",c.Id,"hostId",c.Host,"turnLimit",2,"includeOutputs",false,"maxOutputCharsPerItem",1200),15000);c.Title=J.Str(J.Get(detail,"thread"),"title");c.Detail(detail);Cards[key]=c;keys.Add(key);}catch{if(Cards.ContainsKey(key)){keys.Add(key);c.Stale=true;}}}
   Unavailable=J.Arr(J.Get(result,"unavailableHosts")).Length>0?"部分主机暂不可用":"";
   // Preserve last known remote cards when a host is disconnected.
   foreach(var k in Cards.Keys.ToArray())if(!keys.Contains(k)&&!Cards[k].Live){if(Unavailable.Length>0)Cards[k].Stale=true;else Cards.Remove(k);}
   Connected=true;Connection="已连接 Codex";
   // wait_threads must not wait on its own routing task. Read that one directly.
   var targets=Cards.Values.Where(c=>keys.Contains(c.Key)&&c.Id!=Bridge.ContextId).ToArray();
   for(int start=0;start<targets.Length;start+=8){var group=targets.Skip(start).Take(8).ToArray();try {
    var snapshot=await Call("wait_threads",J.Obj("targets",group.Select(c=>Target(c)).ToArray(),"timeoutMs",0),15000);
    var seen=new HashSet<string>();foreach(var p in J.Arr(J.Get(snapshot,"polls"))){var th=J.Get(p,"thread");string key=J.Str(th,"hostId")+"/"+J.Str(th,"id");Card c;if(Cards.TryGetValue(key,out c)){c.Snapshot(p);seen.Add(key);}}
    // A completed target can wake the batch before other snapshots arrive.
    // Absence is not a disconnect: fetch missing targets individually.
    await Task.WhenAll(group.Where(c=>!seen.Contains(c.Key)).Select(async c=>{try{var single=await Call("wait_threads",J.Obj("targets",new[]{Target(c)},"timeoutMs",0),15000);var polls=J.Arr(J.Get(single,"polls"));if(polls.Length>0)c.Snapshot(polls[0]);else c.Stale=true;}catch{c.Stale=true;}}));
   }catch{foreach(var c in group)c.Stale=true;}}
   foreach(var c in Card.Sort(Cards.Values,Settings).Concat(Cards.Values.Where(c=>c.Status=="active"||c.Id==Bridge.ContextId)).Distinct()){if(J.Now-c.DetailFetched<5&&c.Message.Length>0)continue;try {c.Detail(await Call("read_thread",J.Obj("threadId",c.Id,"hostId",c.Host,"turnLimit",2,"includeOutputs",false,"maxOutputCharsPerItem",1200),15000));}catch{c.DetailFetched=J.Now;}}
   foreach(var c in Card.Sort(Cards.Values,Settings).Where(c=>c.Host=="local"&&!c.Live)){var msg=await Task.Run(()=>localMessages.Read(c));if(msg.Length>0){c.Message=msg;c.MessageKind="最新对话";c.MessageUnavailable=false;c.QuestionPending=localMessages.Waiting(c);}}LastRefresh=DateTime.Now;Connection="已连接 Codex";if(Unavailable.Length>0)Connection=Unavailable;
  }catch(Exception){Connected=false;Connection="Codex 未连接 · 等待重连";Cards=this.Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());foreach(var c in Cards.Values)c.Stale=true;Bridge.Reset();}finally{MergeStream(Cards);if(Stream!=null)Stream.Watch(Cards.Values);this.Cards=Cards;this.Connected=Connected;this.Connection=Connection;this.Unavailable=Unavailable;Refreshing=false;Changed();}}
  public async Task RefreshUsage(){if(refreshingUsage)return;refreshingUsage=true;try{Usage.Update(await Call("get_usage_limits",J.Obj(),15000));}catch{Usage.Error="额度暂不可读取";}finally{refreshingUsage=false;Changed();}}
  public async Task Open(Card card){var window=CodexForeground.Find();bool activated=CodexForeground.Activate(window);await Call("navigate_to_codex_page",J.Obj("threadId",card.Id));if(!activated&&!CodexForeground.Activate(window))throw new IOException("已切换线程，但 Windows 未允许激活 Codex 窗口。");Settings.Clicks[card.Key]=J.Now;Settings.Save();Changed();}
  static object Target(Card c){return c.Cursor.Length>0?J.Obj("threadId",c.Id,"hostId",c.Host,"afterCursor",c.Cursor):J.Obj("threadId",c.Id,"hostId",c.Host);}
 }
}








