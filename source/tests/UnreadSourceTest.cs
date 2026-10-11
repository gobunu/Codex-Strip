using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
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
  public string TurnId=FirstTurn,Status="completed";
  public Func<object,Task<object>> DelayList;
  object Thread(){return J.Obj("id",Id,"hostId",Host,"title","Fixture task","status",J.Obj("type",Status=="completed"?"idle":"active","activeFlags",new string[0]));}
  object Turn(){return J.Obj("id",TurnId,"status",Status=="active"?"inProgress":Status,"startedAt",TurnId==FirstTurn?100:200,"items",new[]{J.Obj("type","agentMessage","text","Fixture reply")});}
  public Task<object> Call(string tool,object args,int timeout){
   object result;
   if(tool=="list_threads")result=J.Obj("pinnedThreads",new object[0],"threads",new[]{J.Obj("id",Id,"hostId",Host,"kind","codex","title","Fixture task","status",Status,"isUnread",Unread,"updatedAt",100)},"unavailableHosts",new object[0]);
   else if(tool=="wait_threads")result=J.Obj("polls",new[]{J.Obj("thread",Thread(),"latestTurn",Turn())});
   else if(tool=="read_thread")result=J.Obj("thread",Thread(),"turns",new[]{Turn()});
   else throw new Exception("Unexpected tool request: "+tool);
   if(tool=="list_threads"&&DelayList!=null)return DelayList(result);
   return Task.FromResult(result);
  }
 }
 static void FreshListStillUpdates(){
  // A list can initialize an unknown card; merely polling its old value again
  // must not manufacture newer evidence than a desktop read acknowledgement.
  foreach(bool unread in new[]{false,true})using(var desktop=new DesktopStream()){
   var tools=new ToolFixture{Unread=unread};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);var current=engine.Cards[Host+"/"+Id];
   Require(current.UnreadKnown&&current.Unread==unread&&current.UnreadResult==unread,"a list did not initialize an unknown card's unread state");
   if(unread)continue;
   tools.TurnId=NextTurn;tools.Unread=true;Refresh(engine);
   current=engine.Cards[Host+"/"+Id];
   Require(current.TurnId==NextTurn&&current.UnreadResult,"the new turn's listed unread result was lost while replacing the previous read turn");
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

 static object FixtureIdentity(){return J.Obj("kind","chatgpt","accountId","fixture-account","userId","fixture-user");}
 static string FixtureIdentityKey(){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(J.Json(new[]{"chatgpt","fixture-account","fixture-user"})))).Replace("-","").ToLowerInvariant();}
 static Dictionary<string,object> ScopedDesktop(bool unread,bool? cachedUnread,string cachedStatus=""){
  var data=Desktop(unread,cachedUnread);
  var identities=(Dictionary<string,object>)J.Get(J.Get(data,"electron-thread-read-state-v1"),"unreadByIdentity");
  object scope=identities["account"];identities.Clear();identities[FixtureIdentityKey()]=scope;
  if(cachedUnread.HasValue&&cachedStatus.Length>0){
   var summary=(Dictionary<string,object>)J.Arr(J.Get(J.Get(data,"electron-persisted-atom-state"),"remote-thread-summaries-v3:"+Host))[0];
   summary["threadRuntimeStatus"]=J.Obj("type",cachedStatus);
  }
  return data;
 }
 static void Deliver(DesktopStream desktop,object msg){
  var handle=typeof(DesktopStream).GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic);
  Require(handle!=null,"desktop stream message handler was unavailable");
  ((Task)handle.Invoke(desktop,new[]{msg})).GetAwaiter().GetResult();
 }
 static void ConnectFixture(DesktopStream desktop){
  // Handle initialization alone has no pipe to write to. Start/Run are never
  // invoked, so these messages cannot reach a running desktop application.
  Deliver(desktop,J.Obj("type","response","method","initialize","result",J.Obj("clientId","fixture-client")));
 }
 static void Receipt(DesktopStream desktop,bool unread){
  Deliver(desktop,J.Obj("type","broadcast","method","thread-read-state-changed","version",3,
   "params",J.Obj("hostId",Host,"conversationId",Id,"hasUnreadTurn",unread,"context",J.Obj("identity",FixtureIdentity(),"executionHostKey",Host+":machine"))));
 }
 static void StreamSnapshot(DesktopStream desktop,string turn,bool? unread,int revision,string status="completed"){
  var state=J.Obj("title","Fixture task","turns",new[]{J.Obj("turnId",turn,"turnStartedAtMs",turn==FirstTurn?100000:200000,"status",status,"items",new[]{J.Obj("type","agentMessage","text","Fixture reply")})});
  if(unread.HasValue)state["hasUnreadTurn"]=unread.Value;
  Deliver(desktop,J.Obj("type","broadcast","method","thread-stream-state-changed","version",11,"sourceClientId","fixture-owner",
   "params",J.Obj("hostId",Host,"conversationId",Id,"change",J.Obj("type","snapshot","revision",revision,"conversationState",state))));
 }
 static void StreamStatusPatch(DesktopStream desktop,int baseRevision,int revision,string status){
  Deliver(desktop,J.Obj("type","broadcast","method","thread-stream-state-changed","version",11,"sourceClientId","fixture-owner",
   "params",J.Obj("hostId",Host,"conversationId",Id,"change",J.Obj("type","patches","baseRevision",baseRevision,"revision",revision,
    "patches",new[]{J.Obj("op","replace","path",new object[]{"turns",0,"status"},"value",status)}))));
 }
 static void Refresh(MonitorEngine engine){engine.Refresh().GetAwaiter().GetResult();Require(engine.Connected,"the isolated fake-tool refresh failed");}
 static Card Current(MonitorEngine engine){return engine.Cards[Host+"/"+Id];}
 static void ReadEveryPhase(MonitorEngine engine,string message){
  engine.ApplyStream();Read(Current(engine),message+" after ApplyStream");
  Refresh(engine);Read(Current(engine),message+" after Refresh");
  engine.ApplyStream();Read(Current(engine),message+" after the subsequent ApplyStream");
 }
 static void PersistentCacheReadSurvivesContinuousStaleLists(){
  foreach(string status in new[]{"completed","idle"})using(var desktop=new DesktopStream()){
   var saved=ScopedDesktop(true,false,status);desktop.ObserveDesktopReadState(saved);ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=true};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);Read(Current(engine),"a completed "+status+" cache checkpoint lost to the stale listed unread value");
   for(int round=0;round<3;round++){
    saved["unrelated-window-size"]=J.Obj("width",1400+round,"height",900);
    desktop.ObserveDesktopReadState(saved);
    ReadEveryPhase(engine,"the completed "+status+" cache checkpoint reverted in refresh round "+round);
   }
  }
 }
 static void ListedUnreadClearsWhenLiveSnapshotReads(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(ScopedDesktop(true,null));ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=true};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);Require(Current(engine).UnreadResult,"the initial listed unread fixture was absent");
   StreamSnapshot(desktop,FirstTurn,false,1);
   for(int round=0;round<3;round++)ReadEveryPhase(engine,"the newest live read snapshot lost to an old listed value in round "+round);
  }
 }
 static void ReceiptReadSurvivesListsAndExplicitUnreadRestores(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(ScopedDesktop(true,null));ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=true};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);Require(Current(engine).UnreadResult,"the initial listed unread fixture was absent");
   Receipt(desktop,false);
   for(int round=0;round<3;round++)ReadEveryPhase(engine,"a dedicated read receipt lost to a stale listed value in round "+round);
   Receipt(desktop,true);engine.ApplyStream();
   Require(Current(engine).UnreadResult,"an explicit same-turn mark-unread receipt could not restore the notification");
   Refresh(engine);Require(Current(engine).UnreadResult,"refresh lost the explicit same-turn mark-unread receipt");
   Receipt(desktop,false);engine.ApplyStream();Read(Current(engine),"the final read receipt did not clear the first turn");
   tools.TurnId=NextTurn;StreamSnapshot(desktop,NextTurn,true,1);engine.ApplyStream();
   Require(Current(engine).TurnId==NextTurn&&Current(engine).UnreadResult,"a genuine new-turn streamed unread result was hidden by the previous receipt");
   Refresh(engine);engine.ApplyStream();
   Require(Current(engine).UnreadResult,"refresh lost the genuine new-turn unread result");
  }
 }
 static void WatchTracksTheTurnThatReceiptAcknowledges(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(ScopedDesktop(true,null));ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=true};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);Require(Current(engine).TurnId==FirstTurn,"first turn was not watched");
   tools.TurnId=NextTurn;Refresh(engine);
   Require(Current(engine).TurnId==NextTurn&&Current(engine).UnreadResult,"the watched next-turn unread fixture was absent");
   // A v3 receipt carries no turn ID. It must bind the current watched turn,
   // rather than the first card ever registered with Watch.
   Receipt(desktop,false);engine.ApplyStream();
   Read(Current(engine),"the receipt acknowledged an obsolete wanted turn instead of the current turn");
   var oldTurn=Project(FirstTurn,"completed",true);Apply(desktop,oldTurn);
   Require(oldTurn.UnreadResult,"the current-turn receipt was incorrectly bound to the first watched turn");
   tools.TurnId="third-turn";StreamSnapshot(desktop,tools.TurnId,true,1);engine.ApplyStream();
   Require(Current(engine).UnreadResult,"a once-bound wanted-turn receipt leaked into another new turn");
  }
 }
 static void CachedReadCheckpointDoesNotRebindToNextPolledTurn(){
  foreach(string status in new[]{"completed","idle"})using(var desktop=new DesktopStream()){
   var saved=ScopedDesktop(true,false,status);desktop.ObserveDesktopReadState(saved);
   var tools=new ToolFixture{Unread=false};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);Read(Current(engine),"the polled first-turn read checkpoint fixture was absent");
   tools.TurnId=NextTurn;tools.Unread=true;Refresh(engine);
   Require(Current(engine).TurnId==NextTurn&&Current(engine).UnreadResult,"the previous cached read checkpoint hid the newly polled unread turn");
   for(int round=0;round<3;round++){
    saved["unrelated-window-size"]=J.Obj("width",1600+round,"height",900);desktop.ObserveDesktopReadState(saved);
    engine.ApplyStream();Require(Current(engine).UnreadResult,"old cache content rebound its read checkpoint to the next polled turn");
    Refresh(engine);engine.ApplyStream();Require(Current(engine).UnreadResult,"refresh rebound the old cache read checkpoint to the next polled turn");
   }
  }
 }
 static void ConfirmedStreamReadSurvivesStaleTrueAndLaterRealRead(){
  using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(ScopedDesktop(true,null));ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=true};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);StreamSnapshot(desktop,FirstTurn,true,1);engine.ApplyStream();
   Require(Current(engine).UnreadResult,"the initial stream unread fixture was absent");
   StreamSnapshot(desktop,FirstTurn,false,2);engine.ApplyStream();
   Read(Current(engine),"the stream read transition did not clear the original reminder");
   Require(Current(engine).ReadConfirmed,"the stream read fixture never reached a confirmed completed result");
   var previous=Current(engine).Copy();
   for(int round=0;round<3;round++){
    StreamSnapshot(desktop,FirstTurn,true,3+round);
    var latest=desktop.Latest()[0];var merged=MonitorEngine.PreferFresh(previous,latest);
    Read(merged,"PreferFresh lost the previous Stream false confirmation to a same-turn stale true snapshot");
    ReadEveryPhase(engine,"a same-turn stale true stream snapshot resurrected the read result in round "+round);
    previous=Current(engine).Copy();
   }
   Receipt(desktop,true);engine.ApplyStream();Require(Current(engine).UnreadResult,"a later manual mark-unread could not restore the stream-confirmed result");
   for(int round=0;round<3;round++){
    StreamSnapshot(desktop,FirstTurn,false,6+round);engine.ApplyStream();
    Require(Current(engine).UnreadResult,"the old stream false replay cleared a newer manual mark-unread before its native acknowledgement");
    Refresh(engine);engine.ApplyStream();Require(Current(engine).UnreadResult,"refresh cleared the manual unread while stream read content remained stale");
   }
   // Native confirmation of the manual unread supplies a new true checkpoint.
   // A following genuine true->false transition can acknowledge that result.
   StreamSnapshot(desktop,FirstTurn,true,9);engine.ApplyStream();Require(Current(engine).UnreadResult,"the native manual-unread acknowledgement disappeared");
   StreamSnapshot(desktop,FirstTurn,false,10);engine.ApplyStream();
   Read(Current(engine),"a genuine stream read after native manual-unread confirmation did not clear the result");
   ReadEveryPhase(engine,"the latest genuine stream read did not survive refresh");
  }
 }
 static void ReadDuringInFlightListRemainsRead(){
  foreach(string source in new[]{"cache","receipt","stream"})using(var desktop=new DesktopStream()){
   var saved=ScopedDesktop(true,source=="cache"?(bool?)true:null,"completed");desktop.ObserveDesktopReadState(saved);ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=true};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);if(source=="stream"){StreamSnapshot(desktop,FirstTurn,true,1);engine.ApplyStream();}
   Require(Current(engine).UnreadResult,"the in-flight list fixture was not unread");
   var resultReady=new TaskCompletionSource<object>();var releaseList=new TaskCompletionSource<object>();
   tools.DelayList=result=>{resultReady.SetResult(result);return releaseList.Task;};
   Task refresh=engine.Refresh();Require(resultReady.Task.IsCompleted&&!refresh.IsCompleted,"the fake list request did not remain in flight");
   if(source=="cache")desktop.ObserveDesktopReadState(ScopedDesktop(true,false,"completed"));
   else if(source=="receipt")Receipt(desktop,false);
   else StreamSnapshot(desktop,FirstTurn,false,2);
   engine.ApplyStream();Read(Current(engine),"the "+source+" read checkpoint was absent while the old list was in flight");
   // The pending refresh owns an older detached card and returns unread=true.
   // Its final merge must retain the read event received after it began.
   releaseList.SetResult(resultReady.Task.Result);refresh.GetAwaiter().GetResult();tools.DelayList=null;
   Require(engine.Connected,"the delayed isolated fake-tool refresh failed");
   Read(Current(engine),"finishing an older in-flight list resurrected unread after a "+source+" read event");
   ReadEveryPhase(engine,"the in-flight "+source+" acknowledgement was lost by a later refresh");
  }
 }
 static void ActiveFalseStatusChangeDoesNotUndoManualUnread(){
  foreach(bool useCache in new[]{false,true})using(var desktop=new DesktopStream()){
   var saved=ScopedDesktop(true,useCache?(bool?)false:null,"active");desktop.ObserveDesktopReadState(saved);ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=false,Status="active"};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);if(!useCache){StreamSnapshot(desktop,FirstTurn,false,1,"inProgress");engine.ApplyStream();}
   Require(Current(engine).Status=="active"&&!Current(engine).Unread,"the old active false checkpoint fixture was absent");
   Receipt(desktop,true);engine.ApplyStream();Require(Current(engine).Unread,"the active manual-unread receipt was not recorded");
   tools.Status="completed";tools.Unread=true;
   if(useCache){
    var summary=(Dictionary<string,object>)J.Arr(J.Get(J.Get(saved,"electron-persisted-atom-state"),"remote-thread-summaries-v3:"+Host))[0];
    summary["threadRuntimeStatus"]=J.Obj("type","completed");desktop.ObserveDesktopReadState(saved);
   }else StreamStatusPatch(desktop,1,2,"completed");
   Refresh(engine);engine.ApplyStream();
   Require(Current(engine).Status=="completed"&&Current(engine).UnreadResult,"the old active false value became a fresh completed read merely because its status changed");
   for(int round=0;round<3;round++){
    desktop.ObserveDesktopReadState(saved);Refresh(engine);engine.ApplyStream();
    Require(Current(engine).UnreadResult,"replaying a status-only false checkpoint cleared the newer manual unread receipt");
   }
  }
 }
 static void StaleListFalseCannotConfirmARealStreamUnread(){
  foreach(bool nextTurn in new[]{false,true})using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(ScopedDesktop(true,null));ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=false};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   desktop.Watch(new[]{new Card{Host=Host,Id=Id,TurnId=FirstTurn,Started=100}});
   StreamSnapshot(desktop,FirstTurn,!nextTurn,1);engine.ApplyStream();
   if(nextTurn){
    Read(Current(engine),"the read first-turn fixture was absent");Refresh(engine);
    tools.TurnId=NextTurn;StreamSnapshot(desktop,NextTurn,true,2);engine.ApplyStream();
   }
   Require(Current(engine).UnreadResult,"the genuinely unread stream fixture was absent before polling the stale false list");
   for(int round=0;round<3;round++){
    Refresh(engine);engine.ApplyStream();
    Require(Current(engine).TurnId==tools.TurnId&&Current(engine).UnreadResult,"a stale listed false value was promoted to confirmation and hid the "+(nextTurn?"new-turn":"initial")+" real stream unread result");
   }
  }
 }
 static void InvalidOldPreviewCannotConsumeCurrentTurnCacheRead(){
  foreach(bool invalidate in new[]{false,true})using(var desktop=new DesktopStream()){
   desktop.ObserveDesktopReadState(ScopedDesktop(true,true,"completed"));ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=true};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);StreamSnapshot(desktop,FirstTurn,true,1);engine.ApplyStream();
   Require(Current(engine).TurnId==FirstTurn&&Current(engine).UnreadResult,"the original live first-turn fixture was absent");
   // An old turn can remain resident while its owner is connected, or after
   // an explicit owner disconnect invalidates that same preview.
   if(invalidate)Deliver(desktop,J.Obj("type","broadcast","method","client-status-changed","params",J.Obj("status","disconnected","clientId","fixture-owner")));
   tools.TurnId=NextTurn;Refresh(engine);
   Require(Current(engine).TurnId==NextTurn&&!Current(engine).Stale&&Current(engine).UnreadResult,"the new polled turn did not replace the "+(invalidate?"invalid":"connected")+" old preview");
   var saved=ScopedDesktop(true,false,"completed");desktop.ObserveDesktopReadState(saved);
   engine.ApplyStream();
   Require(Current(engine).TurnId==NextTurn,"the old preview replaced the current polled turn");
   Read(Current(engine),"Latest consumed the new unbound cache read by binding it to its "+(invalidate?"invalid":"connected")+" old first-turn preview");
   for(int round=0;round<3;round++){
    desktop.ObserveDesktopReadState(saved);ReadEveryPhase(engine,"the current-turn cache read was lost behind the old preview in round "+round);
    Require(Current(engine).TurnId==NextTurn,"a later merge reverted to the old first-turn preview");
   }
  }
 }
 static void OlderUnreadReceiptYieldsToFreshReadAndManualUnreadRestores(){
  foreach(bool useCache in new[]{false,true})using(var desktop=new DesktopStream()){
   var saved=ScopedDesktop(true,useCache?(bool?)true:null,"active");desktop.ObserveDesktopReadState(saved);ConnectFixture(desktop);
   var tools=new ToolFixture{Unread=true,Status="active"};var engine=new MonitorEngine(new Settings(),tools.Call){Stream=desktop};
   Refresh(engine);Require(Current(engine).Status=="active","the active first-turn fixture was absent");
   StreamSnapshot(desktop,FirstTurn,null,1,"inProgress");engine.ApplyStream();
   Require(Current(engine).Status=="active"&&!Current(engine).UnreadResult,"an active unknown-read snapshot produced a completion reminder");
   tools.Status="completed";StreamSnapshot(desktop,FirstTurn,true,2);Refresh(engine);engine.ApplyStream();
   Require(Current(engine).UnreadResult,"active-to-completed true snapshot did not produce the original reminder");
   Receipt(desktop,true);engine.ApplyStream();Require(Current(engine).UnreadResult,"the historical completion receipt fixture was absent");
   if(useCache){saved=ScopedDesktop(true,false,"completed");desktop.ObserveDesktopReadState(saved);}
   else StreamSnapshot(desktop,FirstTurn,false,3);
   engine.ApplyStream();Read(Current(engine),"a fresh "+(useCache?"cached":"streamed")+" read change lost to the older true receipt");
   for(int round=0;round<3;round++){
    saved["unrelated-window-size"]=J.Obj("width",1500+round,"height",900);desktop.ObserveDesktopReadState(saved);
    // Replayed stream values and cache content retain their original evidence
    // age; a new revision or global file rewrite alone is not a new read event.
    StreamSnapshot(desktop,FirstTurn,!useCache?false:true,4+round);
    ReadEveryPhase(engine,"the fresh read checkpoint reverted to the old true receipt in round "+round);
   }
   Receipt(desktop,true);engine.ApplyStream();Require(Current(engine).UnreadResult,"a newer explicit mark-unread receipt could not override the read checkpoint");
   for(int round=0;round<3;round++){
    desktop.ObserveDesktopReadState(saved);StreamSnapshot(desktop,FirstTurn,!useCache?false:true,7+round);
    engine.ApplyStream();Require(Current(engine).UnreadResult,"replayed old read data undid a newer explicit mark-unread receipt");
    Refresh(engine);engine.ApplyStream();Require(Current(engine).UnreadResult,"refresh undid the newer explicit mark-unread receipt");
   }
  }
 }

 static void ActiveReadDoesNotAcknowledgeAnUnknownCompletedResult(){
  foreach(var source in new[]{UnreadSource.Stream,UnreadSource.Authoritative}){
   var previous=Project(FirstTurn,"inProgress",null);
   previous.SetUnread(false,100,source,FirstTurn);
   var current=MonitorEngine.PreferFresh(previous,Project(FirstTurn,"completed",null));
   var settings=new Settings();MonitorEngine.ReconcileCompletion(settings,previous,current);
   Require(current.UnreadResult&&current.CompletionPending,"an active-stage "+source+" false hid the later result with unknown read state");
   current.SetUnread(false,200,source,FirstTurn);
   MonitorEngine.ReconcileCompletion(settings,previous,current);
   Require(!current.UnreadResult&&!current.CompletionPending,"a completed-stage "+source+" read did not clear the fallback reminder");
  }
 }
 static int Main(){
  string savedContext=Environment.GetEnvironmentVariable("CODEX_THREAD_ID"),savedDataDir=Settings.DataDir;
  // Bridge's constructor resolves its context from session_index when this
  // variable is absent. Supply a fixture context before creating any engine.
  Environment.SetEnvironmentVariable("CODEX_THREAD_ID","00000000-0000-7000-8000-000000000099");
  Settings.DataDir=Path.Combine(Path.GetTempPath(),"CodexStrip-UnreadSourceTest-"+Guid.NewGuid().ToString("N"));
  try{
  Case("completed cached read survives repeated stale list/refresh/stream phases",PersistentCacheReadSurvivesContinuousStaleLists);
  Case("fresh live read clears an old listed unread card across refreshes",ListedUnreadClearsWhenLiveSnapshotReads);
  Case("v3 receipts override stale lists and explicit/new-turn unread restores reminders",ReceiptReadSurvivesListsAndExplicitUnreadRestores);
  Case("Watch refreshes receipt turn metadata and binds it only once",WatchTracksTheTurnThatReceiptAcknowledges);
  Case("unchanged read cache does not acknowledge the next polled turn without a live stream",CachedReadCheckpointDoesNotRebindToNextPolledTurn);
  Case("confirmed stream read survives stale true and later genuine reads acknowledge manual unread",ConfirmedStreamReadSurvivesStaleTrueAndLaterRealRead);
  Case("read events arriving during an older list request survive its final merge",ReadDuringInFlightListRemainsRead);
  Case("old active false cannot acknowledge manual unread through a status-only change",ActiveFalseStatusChangeDoesNotUndoManualUnread);
  Case("stale listed false cannot confirm initial or new-turn real stream unread",StaleListFalseCannotConfirmARealStreamUnread);
  Case("connected or invalid old stream preview cannot consume a current-turn cached read checkpoint",InvalidOldPreviewCannotConsumeCurrentTurnCacheRead);
  Case("fresh read changes clear historical true receipts and newer manual unread survives replays",OlderUnreadReceiptYieldsToFreshReadAndManualUnreadRestores);
  Case("cached remote-control summary clears stale persisted unread",CachedReadClearsPersistedUnread);
  Case("identical persisted lists and unrelated global-state edits preserve read receipt",RepeatedStoreDoesNotResurrect);
  Case("live read snapshot overrides stale persistent unread",LiveReadOverridesPersistentUnread);
  Case("same-turn stale snapshot cannot undo dedicated receipt",StaleSameTurnSnapshotDoesNotUndoReceipt);
  Case("new turn keeps unread despite previous turn receipts",NewTurnGetsItsOwnUnreadState);
  Case("lists initialize unknown cards and preserve new-turn unread results",FreshListStillUpdates);
  Case("unknown read state retains completion fallback",UnknownReadStateRetainsCompletionFallback);
  Case("unknown hosts and ambiguous accounts cannot acknowledge a completion",UnknownHostDoesNotAcquireReadState);
  Case("old read cache cannot hide a new unread completion",OldReadValueDoesNotHideNewCompletion);
  Case("active-stage read cache preserves later completion fallback",ReadCacheFromActiveTurnDoesNotAcknowledgeItsCompletion);
  Case("v3 read receipts respect desktop identity and execution scope",ReceiptContextIsolation);
  Case("latest same-turn stream read replaces its earlier unread value",LatestStreamReadReplacesEarlierUnread);
  Case("explicit completed cache checkpoint clears the completion fallback",CompletedCacheCheckpointAcknowledgesResult);
  Case("active-stage stream and receipts cannot acknowledge an unknown later result",ActiveReadDoesNotAcknowledgeAnUnknownCompletedResult);
  Require(!Directory.Exists(Settings.DataDir),"an in-memory regression unexpectedly persisted settings");
  Console.WriteLine("PASS: "+passed+" unread-source regression scenarios; no live desktop access");return 0;
 }catch(Exception error){Console.Error.WriteLine("FAIL: "+error);return 1;}
 finally{Environment.SetEnvironmentVariable("CODEX_THREAD_ID",savedContext);Settings.DataDir=savedDataDir;}
 }

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
