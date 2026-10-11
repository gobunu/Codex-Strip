using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CodexStrip;

// In-memory regression fixtures only: no desktop files, IPC, or shell navigation.
// Compile as a console executable referencing the matching CodexStrip.exe build.
class UnreadSourceTest {
 const string Host="remote-control:env_test_machine";
 const string Id="00000000-0000-7000-8000-000000000011";
 const string OtherId="00000000-0000-7000-8000-000000000012";
 const string FirstTurn="first-turn",NextTurn="next-turn";
 static int passed;
 static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Case(string name,Action run){run();passed++;Console.WriteLine("PASS: "+name);}

 static Dictionary<string,object> Desktop(bool unread,bool? cachedUnread){
  var data=J.Obj("electron-thread-read-state-v1",J.Obj("version",1,"unreadByIdentity",J.Obj("account",J.Obj(Host+":machine",unread?new[]{Id}:new string[0]))));
  if(cachedUnread.HasValue)data["electron-persisted-atom-state"]=J.Obj("remote-thread-summaries-v3:"+Host,new[]{J.Obj("conversationId",Id,"hostId",Host,"hasUnreadTurn",cachedUnread.Value)});
  return data;
 }
 static Card Project(string turn,string status,bool? unread,string host=Host,string id=Id){
  var state=J.Obj("title","Fixture task","turns",new[]{J.Obj("turnId",turn,"turnStartedAtMs",turn==FirstTurn?100000:200000,"status",status,"items",new[]{J.Obj("type","agentMessage","text","Fixture reply")})});
  if(unread.HasValue)state["hasUnreadTurn"]=unread.Value;
  return StreamState.Project(state,host,id);
 }
 static Card Apply(DesktopStream desktop,Card current,Card previous=null,Settings settings=null){
  desktop.ApplyReadState(new[]{current});
  MonitorEngine.ReconcileCompletion(settings??new Settings(),previous,current);
  return current;
 }
 static void Read(Card card,string message){
  Require(card.Status=="completed",message+": completion status changed");
  Require(card.UnreadKnown&&!card.Unread&&!card.UnreadResult,message);
 }

 static void CachedReadClearsPersistedUnread(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(Desktop(true,false));
   var card=Project(FirstTurn,"completed",null);var settings=new Settings();settings.PendingCompletions[card.Key]=FirstTurn;
   Apply(desktop,card,null,settings);
   Read(card,"the remote-control desktop summary did not acknowledge the viewed result");
   Require(!settings.PendingCompletions.ContainsKey(card.Key),"the acknowledged result retained its local completion marker");
  }
 }
 static void RepeatedStoreDoesNotResurrect(){
  using(var desktop=new DesktopStream()){
   var saved=Desktop(true,null);desktop.ObserveDesktopReadState(saved);
   var card=Apply(desktop,Project(FirstTurn,"completed",null));
   Require(card.UnreadResult,"fixture did not begin with a genuinely unread result");
   desktop.ObserveReadState(Host,Id,false,J.Now,FirstTurn);Apply(desktop,card);
   Read(card,"the dedicated desktop receipt did not clear unread");
   desktop.ObserveDesktopReadState(saved);Apply(desktop,card);
   Read(card,"rereading identical persistent content resurrected unread");
   saved["unrelated-window-size"]=J.Obj("width",1400,"height",900);
   desktop.ObserveDesktopReadState(saved);Apply(desktop,card);
   Read(card,"an unrelated global-state edit resurrected unread");
  }
 }
 static void LiveReadOverridesPersistentUnread(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(Desktop(true,null));
   var previous=Apply(desktop,Project(FirstTurn,"completed",null));
   Require(previous.UnreadResult,"fixture persistent unread value was absent");
   var current=MonitorEngine.PreferFresh(previous,Project(FirstTurn,"completed",false));
   Apply(desktop,current,previous);
   Read(current,"a live read snapshot lost to the older persistent unread list");
  }
 }
 static void StaleSameTurnSnapshotDoesNotUndoReceipt(){
  using(var desktop=new DesktopStream()){
   var previous=Project(FirstTurn,"completed",true);
   desktop.ObserveReadState(Host,Id,false,J.Now,FirstTurn);Apply(desktop,previous);
   Read(previous,"receipt fixture remained unread");
   var current=MonitorEngine.PreferFresh(previous,Project(FirstTurn,"completed",true));
   Apply(desktop,current,previous);
   Read(current,"a same-turn conversation snapshot undid a dedicated read receipt");
  }
 }
 static void NewTurnGetsItsOwnUnreadState(){
  using(var desktop=new DesktopStream()){
   var previous=Project(FirstTurn,"completed",false);
   desktop.ObserveReadState(Host,Id,false,J.Now,FirstTurn);Apply(desktop,previous);
   var current=MonitorEngine.PreferFresh(previous,Project(NextTurn,"completed",true));
   Apply(desktop,current,previous);
   Require(current.TurnId==NextTurn&&current.UnreadKnown&&current.UnreadResult,"a previous turn's read receipt hid a new completed unread turn");
  }
  // Four-argument callers do not yet know a turn ID. The first observed card
  // supplies it; the receipt must not follow the thread into later turns.
  using(var desktop=new DesktopStream()){
   var previous=Project(FirstTurn,"completed",true);
   desktop.ObserveReadState(Host,Id,false,J.Now);Apply(desktop,previous);
   Read(previous,"an unbound receipt did not acknowledge the first observed turn");
   var current=MonitorEngine.PreferFresh(previous,Project(NextTurn,"completed",true));
   Apply(desktop,current,previous);
   Require(current.UnreadResult,"an initially unbound receipt leaked into a later turn");
  }
 }

 // Exercise the actual list_threads -> wait_threads -> read_thread refresh
 // path using deterministic tool responses, including successive list values.
 sealed class ToolFixture {
  public bool Unread;
  object Thread(){return J.Obj("id",Id,"hostId",Host,"title","Fixture task","status",J.Obj("type","idle","activeFlags",new string[0]));}
  object Turn(){return J.Obj("id",FirstTurn,"status","completed","startedAt",100,"items",new[]{J.Obj("type","agentMessage","text","Fixture reply")});}
  public Task<object> Call(string tool,object args,int timeout){
   object result;
   if(tool=="list_threads")result=J.Obj("pinnedThreads",new object[0],"threads",new[]{J.Obj("id",Id,"hostId",Host,"kind","codex","title","Fixture task","status","completed","isUnread",Unread,"updatedAt",100)},"unavailableHosts",new object[0]);
   else if(tool=="wait_threads")result=J.Obj("polls",new[]{J.Obj("thread",Thread(),"latestTurn",Turn())});
   else if(tool=="read_thread")result=J.Obj("thread",Thread(),"turns",new[]{Turn()});
   else throw new Exception("Unexpected tool request: "+tool);
   return Task.FromResult(result);
  }
 }
 static void FreshListStillUpdates(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(Desktop(true,false));
   var tools=new ToolFixture{Unread=false};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   engine.Refresh().GetAwaiter().GetResult();Require(engine.Connected,"the fake tool refresh failed");
   Read(engine.Cards[Host+"/"+Id],"a fresh list read value did not reach the card");
   tools.Unread=true;engine.Refresh().GetAwaiter().GetResult();
   Require(engine.Connected&&engine.Cards[Host+"/"+Id].UnreadResult,"a fresh list unread value was hidden by an older read value");
   tools.Unread=false;engine.Refresh().GetAwaiter().GetResult();
   Require(engine.Connected,"the subsequent fake tool refresh failed");
   Read(engine.Cards[Host+"/"+Id],"a subsequent fresh list read value did not clear unread");
  }
 }
 static void UnknownReadStateRetainsCompletionFallback(){
  using(var desktop=new DesktopStream()){
   var settings=new Settings();var previous=Project(NextTurn,"inProgress",null);var current=Project(NextTurn,"completed",null);
   Apply(desktop,current,previous,settings);
   Require(!current.UnreadKnown&&current.CompletionPending&&current.UnreadResult,"unknown desktop read state lost the completion fallback");
   Require(settings.PendingCompletions.ContainsKey(current.Key),"the fallback completion was not recorded");
  }
 }
 static void UnknownHostDoesNotAcquireReadState(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(Desktop(false,false));
   const string unknown="remote-control:env_other_machine";
   var settings=new Settings();var previous=Project(NextTurn,"inProgress",null,unknown);var current=Project(NextTurn,"completed",null,unknown);
   Apply(desktop,current,previous,settings);
   Require(!current.UnreadKnown&&current.UnreadResult,"read metadata for another host hid a new completion");
   // An omitted account scope is not a read acknowledgement either.
   var malformed=Desktop(false,null);var identities=(Dictionary<string,object>)J.Get(J.Get(malformed,"electron-thread-read-state-v1"),"unreadByIdentity");
   identities["another-account"]=J.Obj(Host+":machine",new[]{OtherId});
   desktop.ObserveDesktopReadState(malformed);
   current=Project(NextTurn,"completed",null);previous=Project(NextTurn,"inProgress",null);
   Apply(desktop,current,previous,new Settings());
   Require(!current.UnreadKnown&&current.UnreadResult,"ambiguous account metadata silently acknowledged a completion");
  }
 }
 static void OldReadValueDoesNotHideNewCompletion(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(Desktop(false,false));
   var previous=Apply(desktop,Project(FirstTurn,"completed",null));
   Read(previous,"old cache fixture was not read");
   var current=MonitorEngine.PreferFresh(previous,Project(NextTurn,"completed",true));
   Apply(desktop,current,previous);
   Require(current.UnreadResult,"the old cached read value hid a new unread completion");
  }
 }
 static void ReadCacheFromActiveTurnDoesNotAcknowledgeItsCompletion(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(Desktop(false,false));
   var settings=new Settings();var previous=Apply(desktop,Project(NextTurn,"inProgress",null));
   Require(previous.UnreadKnown&&!previous.Unread,"the active read-cache fixture was absent");
   var current=MonitorEngine.PreferFresh(previous,Project(NextTurn,"completed",null));
   Apply(desktop,current,previous,settings);
   Require(current.CompletionPending&&current.UnreadResult,"an active-stage cached read value acknowledged the later completed result");
   var completed=current.Copy();Apply(desktop,current,completed,settings);
   Require(current.CompletionPending&&current.UnreadResult,"reapplying the old active-stage cache cleared the completion fallback");
  }
 }

 static int Main(){try{
  Case("cached remote-control summary clears stale persisted unread",CachedReadClearsPersistedUnread);
  Case("identical persisted lists and unrelated global-state edits preserve read receipt",RepeatedStoreDoesNotResurrect);
  Case("live read snapshot overrides stale persistent unread",LiveReadOverridesPersistentUnread);
  Case("same-turn stale snapshot cannot undo dedicated receipt",StaleSameTurnSnapshotDoesNotUndoReceipt);
  Case("new turn keeps unread despite previous turn receipts",NewTurnGetsItsOwnUnreadState);
  Case("successive fresh list values still update in full refresh",FreshListStillUpdates);
  Case("unknown read state retains completion fallback",UnknownReadStateRetainsCompletionFallback);
  Case("unknown hosts and ambiguous accounts cannot acknowledge a completion",UnknownHostDoesNotAcquireReadState);
  Case("old read cache cannot hide a new unread completion",OldReadValueDoesNotHideNewCompletion);
  Case("active-stage read cache preserves later completion fallback",ReadCacheFromActiveTurnDoesNotAcknowledgeItsCompletion);
  Case("v3 read receipts respect desktop identity and execution scope",ReceiptContextIsolation);
  Case("latest same-turn stream read replaces its earlier unread value",LatestStreamReadReplacesEarlierUnread);
  Case("explicit completed cache checkpoint clears the completion fallback",CompletedCacheCheckpointAcknowledgesResult);
  Console.WriteLine("PASS: "+passed+" unread-source regression scenarios; no live desktop access");return 0;
 }catch(Exception error){Console.Error.WriteLine("FAIL: "+error);return 1;}}

 static void ReceiptContextIsolation(){
  var identity=J.Obj("kind","chatgpt","accountId","fixture-account","userId","fixture-user");
  string identityKey;
  using(var sha=SHA256.Create())identityKey=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(J.Json(new[]{"chatgpt","fixture-account","fixture-user"})))).Replace("-","").ToLowerInvariant();
  var data=Desktop(true,null);
  var identities=(Dictionary<string,object>)J.Get(J.Get(data,"electron-thread-read-state-v1"),"unreadByIdentity");
  object scope=identities["account"];identities.Clear();identities[identityKey]=scope;
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(data);var card=Apply(desktop,Project(FirstTurn,"completed",true));
   var handle=typeof(DesktopStream).GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic);
   Action<object,string,bool> receipt=(account,executionKey,unread)=>{
    var msg=J.Obj("type","broadcast","method","thread-read-state-changed","version",3,"params",J.Obj("hostId",Host,"conversationId",Id,"hasUnreadTurn",unread,"context",J.Obj("identity",account,"executionHostKey",executionKey)));
    ((Task)handle.Invoke(desktop,new[]{msg})).GetAwaiter().GetResult();Apply(desktop,card);
   };
   receipt(identity,Host+":machine",false);Read(card,"matching v3 receipt was rejected");
   receipt(J.Obj("kind","chatgpt","accountId","other-account","userId","fixture-user"),Host+":machine",true);Read(card,"another account's receipt changed the current task");
   receipt(identity,Host+":other-machine",true);Read(card,"another execution scope changed the current task");
  }
 }

 static void LatestStreamReadReplacesEarlierUnread(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(Desktop(true,null));
   var previous=Apply(desktop,Project(FirstTurn,"completed",true));
   var current=MonitorEngine.PreferFresh(previous,Project(FirstTurn,"completed",false));
   Apply(desktop,current,previous);
   Read(current,"the previous streamed unread value overrode the newest streamed read value");
  }
 }
 static void CompletedCacheCheckpointAcknowledgesResult(){
  using(var desktop=new DesktopStream()){
   var data=Desktop(false,false);
   var summary=(Dictionary<string,object>)J.Arr(J.Get(J.Get(data,"electron-persisted-atom-state"),"remote-thread-summaries-v3:"+Host))[0];
   summary["threadRuntimeStatus"]=J.Obj("type","active");desktop.ObserveDesktopReadState(data);
   var settings=new Settings();var previous=Apply(desktop,Project(FirstTurn,"inProgress",null),null,settings);
   var current=MonitorEngine.PreferFresh(previous,Project(FirstTurn,"completed",null));
   Apply(desktop,current,previous,settings);Require(current.CompletionPending,"fixture did not retain the new result's fallback");
   summary["threadRuntimeStatus"]=J.Obj("type","completed");desktop.ObserveDesktopReadState(data);
   Apply(desktop,current,current.Copy(),settings);Read(current,"an explicit completed read checkpoint did not acknowledge the result");
   Require(!current.CompletionPending,"the completed cache checkpoint retained a fallback marker");
  }
 }
}
