using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodexStrip {
 public sealed partial class MonitorEngine {
  const int DetailConcurrency=4;
  const double InactiveAfterSeconds=30,RetrySeconds=15,DetailRetrySeconds=60;
  readonly object refreshGate=new object();
  readonly Func<double> refreshClock;
  int previousRecentLimit=-1,previousRecentHours=-1;
  sealed class RefreshStamp {
   public bool Listed;public double UpdatedAt,ChangedAt,RetryAt,ReadAt,DetailRetryAt;
   public string Status="",DetailFailureTurn="";public bool? Unread;
  }
  readonly Dictionary<string,RefreshStamp> refreshStamps=new Dictionary<string,RefreshStamp>();
  static bool NeedsAttention(Card card){return card.QuestionPending||new[]{"active","input","approval","reconnecting","unknown"}.Contains(card.Status);}
  RefreshStamp Stamp(string key,double now){RefreshStamp stamp;if(!refreshStamps.TryGetValue(key,out stamp))refreshStamps[key]=stamp=new RefreshStamp{ChangedAt=now};return stamp;}
  async Task ReadCardDetails(Card card,RefreshStamp stamp){
   if(refreshClock()>=stamp.DetailRetryAt||stamp.DetailFailureTurn!=card.TurnId){
    try{card.Detail(await Call("read_thread",J.Obj("threadId",card.Id,"hostId",card.Host,"turnLimit",2,"includeOutputs",false,"maxOutputCharsPerItem",1200),15000));stamp.DetailRetryAt=0;stamp.DetailFailureTurn="";return;}
    catch{stamp.DetailRetryAt=refreshClock()+DetailRetrySeconds;stamp.DetailFailureTurn=card.TurnId;}
   }
   // A single turn with large tool/image items may exceed the detail API limit.
   // Compact snapshots can still establish current status without that payload.
   var result=await Call("wait_threads",J.Obj("targets",new[]{J.Obj("threadId",card.Id,"hostId",card.Host)},"timeoutMs",0),15000);
   var poll=J.Arr(J.Get(result,"polls")).FirstOrDefault(p=>J.Str(J.Get(p,"thread"),"id")==card.Id&&J.Str(J.Get(p,"thread"),"hostId")==card.Host);
   if(poll==null||J.Str(J.Get(J.Get(poll,"thread"),"status"),"type").Length==0)throw new System.IO.IOException("No matching compact task snapshot");
   string previousTurn=card.TurnId;card.Snapshot(poll);if(stamp.DetailFailureTurn.Length==0)stamp.DetailFailureTurn=card.TurnId;
   if(card.Status!="active"&&card.Status!="input"||previousTurn!=card.TurnId)card.QuestionPending=false;
   var message=J.Get(poll,"latestAssistantMessage");string text=message as string??J.Str(message,"text");card.MessageUnavailable=string.IsNullOrWhiteSpace(text);
   if(card.Message.Length>4000)card.Message=card.Message.Substring(0,4000)+"…";
   card.DetailFetched=J.Now;
  }
  void PublishCard(Card card,bool connected){
   lock(refreshGate){
    var next=Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());Card current;
    var copy=card.Copy();if(next.TryGetValue(copy.Key,out current)){
     if(current.TurnId.Length>0&&copy.TurnId.Length>0&&current.TurnId!=copy.TurnId&&current.Started>copy.Started)copy=current.Copy();
     else copy.MergeUnread(current);
    }
    next[copy.Key]=copy;MergeStream(next);UpdateCompletions(Cards,next);Cards=next;Connected=connected;LastRefresh=DateTime.Now;
   }
   Changed();
  }
  async Task RefreshSelective(){
   if(Refreshing)return;Refreshing=true;
   var next=Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());var published=new HashSet<string>();
   bool connected=Connected;string connection=Connection,unavailable=Unavailable;
   var completedAtStart=next.Values.Where(c=>c.Status=="completed"&&c.TurnId.Length>0).ToDictionary(c=>c.Key,c=>c.TurnId);
   try{
    double now=refreshClock();
    int recentLimit=Math.Max(1,Math.Min(50,Settings.SyncRecentLimit)),recentHours=Math.Max(0,Math.Min(168,Settings.SyncRecentHours));
    if(previousRecentLimit!=recentLimit||previousRecentHours!=recentHours){foreach(var stamp in refreshStamps.Values){stamp.ChangedAt=now;stamp.RetryAt=0;}previousRecentLimit=recentLimit;previousRecentHours=recentHours;}
    if(fetch==null&&next.Count==0)foreach(var card in CachedDiscovery(recentLimit))next[card.Key]=card;
    var listing=await ObserveRecentList(recentLimit);double observedAt=listing.ObservedAt;
    var result=listing.Result??J.Obj();
    if(listing.Result==null&&fetch==null)foreach(var card in CachedDiscovery(recentLimit))if(!next.ContainsKey(card.Key))next[card.Key]=card;
    lock(refreshGate){foreach(var current in Cards.Values)if(!next.ContainsKey(current.Key))next[current.Key]=current.Copy();MergeStream(next);}
    if(listing.Result!=null)SaveDiscovery(result,recentLimit);
    var offline=new HashSet<string>(J.Arr(J.Get(result,"unavailableHosts")).Select(h=>J.Str(h,"hostId")).Where(h=>h.Length>0));
    var listedUnread=new Dictionary<string,bool>();var listedEnded=new HashSet<string>();var unreadChanges=new HashSet<string>();var keys=new HashSet<string>();
    foreach(var item in J.Arr(J.Get(result,"pinnedThreads")).Concat(J.Arr(J.Get(result,"threads")).Take(recentLimit)).Where(t=>J.Str(t,"kind")=="codex")){
     string id=J.Str(item,"id"),host=J.Str(item,"hostId");if(host.Length==0)host="local";
     string key=host+"/"+id;if(!keys.Add(key))continue;
     string status=J.Str(item,"status");double updated=J.UnixSeconds(J.Num(item,"updatedAt"));
     bool? unread=J.Get(item,"isUnread") is bool?(bool?)J.Get(item,"isUnread"):null;
     var stamp=Stamp(key,now);
     if(stamp.Listed&&stamp.Unread!=unread||!stamp.Listed&&unread==true)unreadChanges.Add(key);
     if(!stamp.Listed||stamp.UpdatedAt!=updated||stamp.Status!=status||stamp.Unread!=unread){stamp.ChangedAt=now;stamp.RetryAt=0;}
     stamp.Listed=true;stamp.UpdatedAt=updated;stamp.Status=status;stamp.Unread=unread;
     Card card;if(!next.TryGetValue(key,out card))next[key]=card=new Card{Id=id,Host=host,Status=status};
     card.Title=J.Str(item,"title");card.Interaction=Math.Max(card.Interaction,updated);card.NavigationMetadata(item);
     if(unread.HasValue)listedUnread[key]=unread.Value;
     if(new[]{"idle","notLoaded","completed"}.Contains(status))listedEnded.Add(key);
    }
    foreach(var watch in Settings.Watched){string key=watch.Value+"/"+watch.Key;keys.Add(key);if(!next.ContainsKey(key))next[key]=new Card{Id=watch.Key,Host=watch.Value,Title=UiText.T("正在读取线程","Loading conversation")};Stamp(key,now);}
    unavailable=offline.Count>0?"部分主机暂不可用":"";
    connected=listing.Result!=null||Connected||Stream!=null&&Stream.Connected;
    if(listing.Error is System.IO.IOException)connected=Stream!=null&&Stream.Connected;
    connection=listing.Notice.Length>0?listing.Notice:unavailable.Length>0?unavailable:"已连接 Codex";
    // Preserve cold cached cards. Falling out of the recent list is not a disconnect.
    foreach(var card in next.Values)if(offline.Contains(card.Host))card.Stale=true;
    Action<Card> applyRead=card=>{
     bool unread;if(!listedUnread.TryGetValue(card.Key,out unread))return;
     Card observation;bool known=listedReadState.TryGetValue(card.Key,out observation);string completedTurn;
     bool completedRead=known&&!unread&&card.Status=="completed"&&listedEnded.Contains(card.Key)&&completedAtStart.TryGetValue(card.Key,out completedTurn)&&completedTurn==card.TurnId&&observation.TurnId==card.TurnId&&(observation.Unread||observation.UnreadStatus!="completed");
     if(!known||observation.TurnId!=card.TurnId||observation.Unread!=unread||completedRead){observation=new Card{Host=card.Host,Id=card.Id,TurnId=card.TurnId,Status=card.Status};observation.SetUnread(unread,observedAt,completedRead?UnreadSource.CompletedList:UnreadSource.List,card.TurnId);listedReadState[card.Key]=observation;}
     card.MergeUnread(observation);
    };
    var shown=new HashSet<string>(Card.Sort(next.Values,Settings).Select(c=>c.Key));
    var queue=next.Values.Where(c=>!offline.Contains(c.Host)&&(keys.Contains(c.Key)||NeedsAttention(c)||c.Stale||shown.Contains(c.Key))).Where(c=>{
      var stamp=Stamp(c.Key,now);double updated=stamp.Listed?stamp.UpdatedAt:c.Interaction;
      bool recent=recentHours==0||updated<=0||now-updated<=recentHours*3600.0;
      bool attention=NeedsAttention(c)&&c.Status!="unknown"||unreadChanges.Contains(c.Key)||Settings.Watched.ContainsKey(c.Id);
      bool listedActive=stamp.ChangedAt==now&&new[]{"active","input","approval","reconnecting"}.Contains(stamp.Status);
      bool visibleFallback=listing.Result==null&&shown.Contains(c.Key)&&now-stamp.ReadAt>=15;
      return now>=stamp.RetryAt&&(recent||attention||listedActive)&&(NeedsAttention(c)||c.Stale||now-stamp.ChangedAt<InactiveAfterSeconds||visibleFallback);
    }).OrderByDescending(c=>shown.Contains(c.Key)).ThenByDescending(NeedsAttention).ThenByDescending(c=>c.Interaction).ToArray();
    using(var slots=new SemaphoreSlim(DetailConcurrency)){
     await Task.WhenAll(queue.Select(async card=>{
      await slots.WaitAsync();try{
       var stamp=Stamp(card.Key,now);
       try{
        // One detail read supplies status and messages. Warm/active tasks still
        // get checked when a cached stream or second-resolution list misses a new turn.
        await ReadCardDetails(card,stamp);
        if(card.Host=="local"&&!card.Live){var message=await Task.Run(()=>localMessages.Read(card));if(message.Length>0){card.Message=message;card.MessageKind="最新对话";card.MessageUnavailable=false;card.QuestionPending=localMessages.Waiting(card);}}
        stamp.RetryAt=0;stamp.ReadAt=refreshClock();lock(refreshGate)connected=true;
       }catch{card.Stale=true;stamp.RetryAt=refreshClock()+RetrySeconds;stamp.DetailRetryAt=0;stamp.DetailFailureTurn="";}
       lock(refreshGate){applyRead(card);published.Add(card.Key);Connection=connection;Unavailable=unavailable;}
       PublishCard(card,connected);
      }finally{slots.Release();}
     }));
    }
    foreach(var card in next.Values)lock(refreshGate)applyRead(card);
    LastRefresh=DateTime.Now;
   }catch(TimeoutException){connected=Connected||(Stream!=null&&Stream.Connected);connection="任务列表超时 · 正在重试";next=Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());foreach(var card in next.Values)card.Stale=true;
   }catch{connected=false;connection="Codex 未连接 · 等待重连";next=Cards.ToDictionary(p=>p.Key,p=>p.Value.Copy());foreach(var card in next.Values)card.Stale=true;Bridge.Reset();
   }finally{
    lock(refreshGate){
     foreach(var current in Cards.Values){Card card;if(!next.TryGetValue(current.Key,out card)||published.Contains(current.Key))next[current.Key]=current.Copy();else card.MergeUnread(current);}
     MergeStream(next);UpdateCompletions(Cards,next);Cards=next;Connected=connected;Connection=connection;Unavailable=unavailable;Refreshing=false;
     WriteSyncDiagnostics();
    }
    Changed();
   }
  }
 }
}
