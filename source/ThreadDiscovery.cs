using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodexStrip {
 public sealed partial class MonitorEngine {
  sealed class ListRequest {public Task<object> Task;public double ObservedAt,StartedAt;}
  sealed class ListObservation {public object Result;public double ObservedAt;public string Notice="";public Exception Error;}
  ListRequest pendingList;double nextListAttempt;int listFailures;string listFailureNotice="";
  async Task<object> RequestList(int limit){return await Call("list_threads",J.Obj("limit",limit),45000);}
  async Task<ListObservation> ObserveRecentList(int limit){
   double now=refreshClock();
   if(pendingList==null&&now>=nextListAttempt)pendingList=new ListRequest{ObservedAt=UnreadClock.Next(),StartedAt=now,Task=RequestList(limit)};
   if(pendingList!=null){
    // A list query can stall in host enumeration. Never hold known task updates
    // behind it; retain one in-flight request and consume its result only once.
    if(!pendingList.Task.IsCompleted)await Task.WhenAny(pendingList.Task,Task.Delay(200));
    if(pendingList.Task.IsCompleted){
     var request=pendingList;pendingList=null;
     try{var result=await request.Task;listFailures=0;nextListAttempt=0;listFailureNotice="";return new ListObservation{Result=result,ObservedAt=request.ObservedAt};}
     catch(Exception error){
      listFailures++;nextListAttempt=refreshClock()+Math.Min(300,30*Math.Pow(2,Math.Min(4,listFailures-1)));
      listFailureNotice=error is TimeoutException?UiText.T("列表超时 · 任务单独更新","List timed out · Updating known tasks"):UiText.T("列表暂不可读 · 任务单独更新","List unavailable · Updating known tasks");
      if(error is IOException)Bridge.Reset();
      return new ListObservation{Error=error,Notice=listFailureNotice};
     }
    }
    return new ListObservation{Notice=now-pendingList.StartedAt>=5?UiText.T("列表读取较慢 · 任务单独更新","Slow task list · Updating known tasks"):Connected?"已连接 Codex":UiText.T("正在读取任务列表…","Loading task list…")};
   }
   return new ListObservation{Notice=listFailureNotice};
  }
  IEnumerable<Card> CachedDiscovery(int limit){
   var cards=new Dictionary<string,Card>();
   try{
    var data=J.Parse(File.ReadAllText(Path.Combine(Settings.DataDir,"recent-thread-cache.json")));
    foreach(var item in J.Arr(J.Get(data,"threads"))){
     Guid id;string value=J.Str(item,"id"),host=J.Str(item,"hostId");if(!Guid.TryParse(value,out id)||host.Length==0)continue;
     var card=new Card{Id=value,Host=host,Title=J.Str(item,"title"),Status=J.Str(item,"status"),Interaction=J.UnixSeconds(J.Num(item,"updatedAt")),Stale=true};cards[card.Key]=card;
    }
   }catch{}
   foreach(var card in DesktopStream.ReadKnownThreadCandidates(limit))if(!cards.ContainsKey(card.Key))cards[card.Key]=card;
   double now=refreshClock();return cards.Values.Where(c=>Settings.SyncRecentHours<=0||c.Interaction<=0||now-c.Interaction<=Settings.SyncRecentHours*3600.0||NeedsAttention(c)&&c.Status!="unknown").OrderByDescending(c=>NeedsAttention(c)&&c.Status!="unknown").ThenByDescending(c=>Card.Rank(c,Settings)).Take(limit).ToArray();
  }
  void SaveDiscovery(object result,int limit){
   if(fetch!=null)return;
   try{
    var threads=J.Arr(J.Get(result,"pinnedThreads")).Concat(J.Arr(J.Get(result,"threads")).Take(limit)).Where(t=>J.Str(t,"kind")=="codex").Select(t=>J.Obj("id",J.Str(t,"id"),"hostId",J.Str(t,"hostId","local"),"title",J.Str(t,"title"),"status",J.Str(t,"status"),"updatedAt",J.UnixSeconds(J.Num(t,"updatedAt")))).ToArray();
    Directory.CreateDirectory(Settings.DataDir);File.WriteAllText(Path.Combine(Settings.DataDir,"recent-thread-cache.json"),J.Json(J.Obj("version",1,"threads",threads)));
   }catch{}
  }
  void WriteSyncDiagnostics(){
   if(fetch!=null)return;
   try{
    double now=refreshClock();var shown=Card.Sort(Cards.Values,Settings);
    var state=J.Obj("version",1,"observedUtc",DateTime.UtcNow.ToString("o"),"connected",Connected,"connection",Connection,"knownThreads",Cards.Count,"shownThreads",shown.Length,"shownStale",shown.Count(c=>c.Stale),"listInFlight",pendingList!=null&&!pendingList.Task.IsCompleted,"listPendingSeconds",pendingList==null?0:Math.Max(0,now-pendingList.StartedAt),"listFailures",listFailures,"listRetrySeconds",Math.Max(0,nextListAttempt-now));
    Directory.CreateDirectory(Settings.DataDir);File.WriteAllText(Path.Combine(Settings.DataDir,"sync-state.json"),J.Json(state));
   }catch{}
  }
 }
}
