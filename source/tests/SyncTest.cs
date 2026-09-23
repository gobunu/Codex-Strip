using System;using System.Threading.Tasks;using CodexStrip;
class SyncTest {
 static object Thread(){return J.Obj("id","test","hostId","remote-test","status",J.Obj("type","active"));}
 static async Task Run(){var gate=new TaskCompletionSource<object>();var reached=new TaskCompletionSource<bool>();bool fail=false;
 var engine=new MonitorEngine(new Settings(),(tool,args,timeout)=>{
  if(fail)throw new Exception("offline");
  if(tool=="list_threads")return Task.FromResult<object>(J.Obj("threads",new[]{J.Obj("id","test","hostId","remote-test","kind","codex","title","new title","status","active")}));
  if(tool=="wait_threads")return Task.FromResult<object>(J.Obj("polls",new[]{J.Obj("thread",Thread(),"latestTurn",J.Obj("id","turn","startedAt",20,"status","inProgress"))}));
  if(tool=="read_thread"){reached.SetResult(true);return gate.Task;}
  throw new Exception(tool);
 });
 var old=new Card{Id="test",Host="remote-test",Title="old title",Message="old message",Status="completed"};engine.Cards[old.Key]=old;int commits=0;engine.Changed=()=>commits++;
 var refresh=engine.Refresh();await reached.Task;
 if(engine.Cards[old.Key]!=old||old.Title!="old title"||old.Status!="completed"||commits!=0)throw new Exception("Partial data escaped while awaiting messages");
 gate.SetResult(J.Obj("thread",Thread(),"turns",new[]{J.Obj("id","turn","startedAt",20,"items",new[]{J.Obj("type","agentMessage","text","new message")})}));await refresh;
 if(commits!=1||engine.Cards[old.Key].Message!="new message"||engine.Cards[old.Key].Title!="new title"||old.Message!="old message")throw new Exception("Atomic commit failed");
 fail=true;await engine.Refresh();if(engine.Cards[old.Key].Message!="new message"||!engine.Cards[old.Key].Stale||commits!=2)throw new Exception("Failure did not preserve content");
 Console.WriteLine("PASS: old snapshot retained during slow reads; one complete commit; old objects unchanged; failure retains messages");
 }
 static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
