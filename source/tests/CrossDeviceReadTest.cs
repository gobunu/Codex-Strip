using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CodexStrip;

class CrossDeviceReadTest {
 const string Host="remote-ssh-discovered:fixture",Id="cross-device",First="first",Next="next";
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
 sealed class Fixture {
  public string Turn=First,ListStatus="active",TurnStatus="inProgress";public bool Unread;
  public Func<object,Task<object>> DelayList;
  public Task<object> Call(string tool,object args,int timeout){
   object result;
   if(tool=="list_threads"){
    result=J.Obj("threads",new[]{J.Obj("id",Id,"hostId",Host,"kind","codex","title","Cross-device fixture","status",ListStatus,"isUnread",Unread)});
    return DelayList==null?Task.FromResult(result):DelayList(result);
   }
   var thread=J.Obj("id",Id,"hostId",Host,"status",J.Obj("type",TurnStatus=="inProgress"?"active":"idle"));
   var turn=J.Obj("id",Turn,"status",TurnStatus,"startedAt",Turn==First?100:200,"items",new object[0]);
   if(tool=="wait_threads")result=J.Obj("polls",new[]{J.Obj("thread",thread,"latestTurn",turn)});
   else if(tool=="read_thread")result=J.Obj("thread",thread,"turns",new[]{turn});
   else throw new Exception(tool);
   return Task.FromResult(result);
  }
 }
 static Card Current(MonitorEngine engine){return engine.Cards[Host+"/"+Id];}
 static void Refresh(MonitorEngine engine){engine.Refresh().GetAwaiter().GetResult();Require(engine.Connected,"refresh failed");}
 static void Read(Card card){Require(card.Status=="completed"&&card.UnreadKnown&&!card.Unread&&!card.CompletionPending&&!card.UnreadResult,"read completed result still blocks idle");}
 static void Deliver(DesktopStream desktop,object message){((Task)typeof(DesktopStream).GetMethod("Handle",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(desktop,new[]{message})).GetAwaiter().GetResult();}
 static void Snapshot(DesktopStream desktop,string turn,bool unread,int revision){
  desktop.Watch(new[]{new Card{Host=Host,Id=Id,TurnId=turn,Status="completed"}});
  Deliver(desktop,J.Obj("type","response","method","initialize","result",J.Obj("clientId","fixture")));
  Deliver(desktop,J.Obj("type","broadcast","method","thread-stream-state-changed","version",11,"sourceClientId","owner","params",J.Obj("hostId",Host,"conversationId",Id,"change",J.Obj("type","snapshot","revision",revision,"conversationState",J.Obj("hasUnreadTurn",unread,"turns",new[]{J.Obj("turnId",turn,"status","completed","turnStartedAtMs",turn==First?100000:200000)})))));
 }
 static void CompletedReadWithoutStream(){
  var fixture=new Fixture();var engine=new MonitorEngine(new Settings(),fixture.Call);
  Refresh(engine);Require(Current(engine).UnreadStatus=="active","active-stage read fixture missing");
  fixture.TurnStatus="completed";Refresh(engine);
  Require(Current(engine).CompletionPending,"an in-flight active list acknowledged a later result");
  fixture.ListStatus="idle";Refresh(engine);Read(Current(engine));
  Require(!engine.Settings.PendingCompletions.ContainsKey(Host+"/"+Id),"pending marker was not removed");
  for(int i=0;i<3;i++){Refresh(engine);Read(Current(engine));}
 }
 static void FreshListChangeOverridesStaleStream(){
  using(var desktop=new DesktopStream()){
   var fixture=new Fixture{Unread=true,ListStatus="idle",TurnStatus="completed"};var engine=new MonitorEngine(new Settings(),fixture.Call){Stream=desktop};
   Snapshot(desktop,First,true,1);Refresh(engine);Require(Current(engine).UnreadResult,"unread fixture missing");
   fixture.Unread=false;Refresh(engine);Read(Current(engine));
   for(int i=0;i<3;i++){engine.ApplyStream();Refresh(engine);Read(Current(engine));}
   desktop.ObserveReadState(Host,Id,true,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0+.001,First);engine.ApplyStream();
   Require(Current(engine).UnreadResult,"manual unread was suppressed");
   Refresh(engine);Require(Current(engine).UnreadResult,"unchanged listed read cleared a newer manual unread");
   fixture.Unread=true;Refresh(engine);fixture.Unread=false;Refresh(engine);Read(Current(engine));
   fixture.Turn=Next;fixture.Unread=false;Snapshot(desktop,Next,true,2);Refresh(engine);
   Require(Current(engine).TurnId==Next&&Current(engine).UnreadResult,"prior read checkpoint acknowledged a new unread turn");
  }
 }
 static void InFlightReadCannotUndoNewerManualUnread(){
  using(var desktop=new DesktopStream()){
   var fixture=new Fixture{Unread=true,ListStatus="idle",TurnStatus="completed"};var engine=new MonitorEngine(new Settings(),fixture.Call){Stream=desktop};
   Snapshot(desktop,First,true,1);Refresh(engine);
   var ready=new TaskCompletionSource<object>();var release=new TaskCompletionSource<object>();fixture.Unread=false;
   fixture.DelayList=result=>{ready.SetResult(result);return release.Task;};var refresh=engine.Refresh();ready.Task.GetAwaiter().GetResult();
   desktop.ObserveReadState(Host,Id,true,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0+.01,First);engine.ApplyStream();
   release.SetResult(ready.Task.Result);refresh.GetAwaiter().GetResult();
   Require(Current(engine).UnreadResult,"older list response cleared a newer manual unread");
  }
 }
 static void FirstCompletedListAfterActiveRead(){
  foreach(var source in new[]{UnreadSource.CachedSummary,UnreadSource.Stream,UnreadSource.Authoritative}){
  var fixture=new Fixture{ListStatus="idle",TurnStatus="completed"};
  var engine=new MonitorEngine(new Settings(),fixture.Call);
  // Discovery or the live stream can publish a running, viewed turn before
  // that task first appears in list_threads. Its result still needs review.
  var running=new Card{Host=Host,Id=Id,TurnId=First,Status="active",Started=100};
  running.SetUnread(false,1,source,First);engine.Cards[running.Key]=running;
  Refresh(engine);Require(Current(engine).CompletionPending,"the first completed list prematurely acknowledged a newly observed result");
  // A later completed-stage list can acknowledge this same result even if
  // the first list checkpoint already said false/completed at lower priority.
  Refresh(engine);Read(Current(engine));
  for(int i=0;i<3;i++){Refresh(engine);Read(Current(engine));}
  fixture.Turn=Next;fixture.Unread=true;Refresh(engine);
  Require(Current(engine).TurnId==Next&&Current(engine).UnreadResult,"the previous read acknowledgement hid a new completed result");
  }
 }
 static int Main(){string data=Settings.DataDir;try{
  Settings.DataDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cross-device-test-data-"+Guid.NewGuid().ToString("N"));
  FirstCompletedListAfterActiveRead();Console.WriteLine("PASS: repeated completed list clears an active read checkpoint even when first discovered after completion");
  CompletedReadWithoutStream();Console.WriteLine("PASS: completed-stage list read clears active-stage fallback without a local stream");
  FreshListChangeOverridesStaleStream();Console.WriteLine("PASS: changed listed read clears stale stream; manual unread and new turns remain visible");
  InFlightReadCannotUndoNewerManualUnread();Console.WriteLine("PASS: in-flight listed read cannot undo a newer manual unread");
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{Settings.DataDir=data;}}
}
