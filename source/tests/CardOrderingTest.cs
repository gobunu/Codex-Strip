using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodexStrip;

// Deterministic in-memory cases; no desktop IPC, navigation, or user files.
class CardOrderingTest {
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static Card CardFor(string id,string status,double when){return new Card{Host="local",Id=id,Title=id,Status=status,Interaction=when};}
 static void MainOrder(){
  var unread=CardFor("unread","completed",10);unread.Unread=true;
  var pending=CardFor("pending","completed",11);pending.CompletionPending=true;
  var question=CardFor("question","active",20);question.QuestionPending=true;
  var input=CardFor("input","input",21);
  var approval=CardFor("approval","approval",30);
  var running=CardFor("running","active",40);
  var done=CardFor("done","completed",1000);
  var cards=new[]{done,running,approval,input,question,unread,pending};
  var settings=new Settings{Count=12};
  Check(CodexStrip.Card.Sort(cards,settings).Select(c=>c.Id).SequenceEqual(new[]{"pending","unread","input","question","approval","running","done"}),"attention priority did not override historical recency");
  settings.Count=4;
  Check(CodexStrip.Card.Sort(cards,settings).Select(c=>c.Id).SequenceEqual(new[]{"pending","unread","input","question"}),"card limit was applied before state priority");
  unread.Unread=false;pending.CompletionPending=false;question.QuestionPending=false;input.Status="completed";
  Check(CodexStrip.Card.Sort(cards,settings)[0]==approval,"cleared attention did not leave the leading groups");
 }
 static void OrdinaryStatesShareOnePriority(){
  var items=new List<Card>();double when=100;
  foreach(string status in new[]{"completed","failed","systemError","reconnecting","interrupted","idle","notLoaded","unknown"})items.Add(CardFor(status,status,when++));
  var stale=CardFor("stale","active",when++);stale.Stale=true;items.Add(stale);
  var capacity=CardFor("capacity","active",when++);capacity.LastError="Model capacity exhausted";items.Add(capacity);
  var missing=CardFor("message-unavailable","completed",when++);missing.MessageUnavailable=true;items.Add(missing);
  Check(items.All(c=>c.SortPriority==4),"ordinary states have different priorities");
  var settings=new Settings{Count=12};
  Check(CodexStrip.Card.Sort(items,settings).SequenceEqual(items.AsEnumerable().Reverse()),"ordinary states were not ordered only by recency");
  var running=CardFor("running","active",1);items.Add(running);
  Check(CodexStrip.Card.Sort(items,settings)[0]==running,"old failure or capacity state blocked a running task");
  var completedCapacity=CardFor("completed-capacity","completed",1000);completedCapacity.Unread=true;completedCapacity.LastError="overloaded";
  Check(completedCapacity.SortPriority==4,"capacity failure was promoted to unread-completion priority");
 }
 static void MixedTimestampUnits(){
  double now=J.Now;var old=CardFor("old","completed",(now-86400)*1000);var recent=CardFor("recent","completed",now-10);
  var settings=new Settings{Count=12};settings.Clicks[old.Key]=(now-7200)*1000;
  Check(CodexStrip.Card.Sort(new[]{old,recent},settings)[0]==recent,"milliseconds or old clicks resurrected an old task");
  settings.Clicks[old.Key]=now;
  Check(CodexStrip.Card.Sort(new[]{old,recent},settings)[0]==old,"valid click recency was lost within the same priority");
  old.Interaction=double.NaN;settings.Clicks.Clear();
  Check(CodexStrip.Card.Sort(new[]{old,recent},settings)[0]==recent,"invalid timestamp outranked a real interaction");
  Check(J.UnixSeconds(double.PositiveInfinity)==0&&J.UnixSeconds(-1)==0&&J.UnixSeconds((now+86400)*1000)==0,"invalid or future timestamp was accepted");
  Check(Math.Abs(J.UnixSeconds((now-86400)*1000)-(now-86400))<.001,"millisecond timestamp conversion changed the instant");
 }
 static void FiltersAndStableTies(){
  var a=CardFor("a","completed",1);var b=CardFor("b","completed",1);var ignored=CardFor("ignored","completed",1000);ignored.Unread=true;
  var remote=CardFor("remote","input",1000);remote.Host="remote-ssh-discovered:fixture";
  var settings=new Settings{Count=12,Host="local"};settings.SleepIgnoreKeywords.Add("ignored");
  Check(CodexStrip.Card.Sort(new[]{remote,ignored,b,a},settings).SequenceEqual(new[]{a,b}),"host/ignore filters or deterministic ties changed");
 }
 static void TimestampIngestion(){
  double now=J.Now;
  var data=J.Obj("electron-persisted-atom-state",J.Obj("remote-thread-summaries-v3:remote-ssh-discovered:fixture",new[]{J.Obj("conversationId","00000000-0000-7000-8000-000000000001","hostId","remote-ssh-discovered:fixture","updatedAt",(now-3600)*1000)}));
  var cached=DesktopStream.KnownThreadCandidates(data).Single();
  Check(Math.Abs(cached.Interaction-(now-3600))<.001,"desktop discovery retained milliseconds");
  var fixture=new Func<string,object,int,Task<object>>((tool,args,timeout)=>{
   string id=J.Str(args,"threadId");
   if(tool=="list_threads")return Task.FromResult<object>(J.Obj("threads",new[]{J.Obj("id","old","hostId","local","kind","codex","title","old","status","idle","updatedAt",(now-3600)*1000),J.Obj("id","recent","hostId","local","kind","codex","title","recent","status","idle","updatedAt",now-10)}));
   if(tool=="read_thread")return Task.FromResult<object>(J.Obj("thread",J.Obj("id",id,"hostId","local","status",J.Obj("type","idle")),"turns",new[]{J.Obj("id",id+"-turn","status","completed","startedAt",id=="old"?(now-3600)*1000:now-10)}));
   throw new Exception("Unexpected tool: "+tool);
  });
  var engine=new MonitorEngine(new Settings(),fixture);engine.Refresh().GetAwaiter().GetResult();
  Check(engine.Connected&&engine.Cards.Count==2,"fixture refresh failed");
  Check(engine.Cards.Values.All(c=>c.Interaction<now+1&&c.Started<now+1),"list or turn ingestion kept a millisecond timestamp");
  Check(CodexStrip.Card.Sort(engine.Cards.Values,engine.Settings)[0].Id=="recent","mixed-unit refreshed cards sorted incorrectly");
 }
 static int Main(){try{MainOrder();OrdinaryStatesShareOnePriority();MixedTimestampUnits();FiltersAndStableTies();TimestampIngestion();Console.WriteLine("PASS: "+checks+" card priority, ordinary-state recency, mixed timestamp, filter, tie and ingestion checks");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
