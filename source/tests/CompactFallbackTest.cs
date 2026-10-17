using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodexStrip;

class CompactFallbackTest {
 const string Host="remote-ssh-discovered:fixture",Id="compact-fixture";
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 sealed class Fixture {
  public double Now=100;public int Details,Compacts;public string Turn="first",Status="active",TurnStatus="inProgress",Text="compact progress";
  public bool BrokenCompact,WrongHost,DetailWorks;public string Flag="";
  public Task<object> Call(string tool,object args,int timeout){
   if(tool=="list_threads")return Task.FromResult<object>(J.Obj("threads",new[]{J.Obj("id",Id,"hostId",Host,"kind","codex","title","Fixture","status",Status,"updatedAt",Now,"isUnread",false)}));
   if(tool=="read_thread"){
    Details++;if(!DetailWorks)throw new IOException("Tool response exceeds the size limit. A single oversized turn cannot be returned.");
    return Task.FromResult<object>(J.Obj("thread",J.Obj("id",Id,"hostId",Host,"status",J.Obj("type",Status)),"turns",new[]{J.Obj("id",Turn,"status",TurnStatus,"startedAt",100,"items",new[]{J.Obj("type","agentMessage","text","full detail recovered")})}));
   }
   if(tool!="wait_threads")throw new Exception("Unexpected tool: "+tool);
   Compacts++;Check(J.Num(args,"timeoutMs")==0&&J.Arr(J.Get(args,"targets")).Length==1,"compact fallback attempted a blocking or multi-host wait");
   if(BrokenCompact)throw new IOException("host unavailable");
   return Task.FromResult<object>(J.Obj("polls",new[]{J.Obj("thread",J.Obj("id",Id,"hostId",WrongHost?"remote-ssh-discovered:other":Host,"status",J.Obj("type",Status,"activeFlags",Flag.Length>0?new[]{Flag}:new string[0])),"latestTurn",J.Obj("id",Turn,"status",TurnStatus,"startedAt",Turn=="first"?100:200),"latestAssistantMessage",Text.Length>0?J.Obj("phase",TurnStatus=="completed"?"final_answer":"commentary","text",Text):null)}));
  }
 }
 static Card Current(MonitorEngine engine){return engine.Cards[Host+"/"+Id];}
 static void OversizedTurnKeepsUpdating(){
  var f=new Fixture();var e=new MonitorEngine(new Settings(),f.Call,()=>f.Now);
  e.Refresh().GetAwaiter().GetResult();var c=Current(e);
  Check(e.Connected&&!c.Stale&&c.Status=="active"&&c.Message=="compact progress"&&!c.MessageUnavailable,"oversized turn lost healthy compact status or progress");
  f.Now+=5;f.Text="new compact progress";e.Refresh().GetAwaiter().GetResult();
  Check(f.Details==1&&f.Compacts==2&&Current(e).Message==f.Text,"large payload was retried immediately or compact updates stopped");
  Current(e).QuestionPending=true;f.Now+=5;f.Status="idle";f.TurnStatus="completed";f.Text="compact final";e.Refresh().GetAwaiter().GetResult();
  Check(!Current(e).Stale&&Current(e).Status=="completed"&&!Current(e).QuestionPending&&Current(e).Message=="compact final","compact completion retained stale/question state");
  f.Now+=61;f.DetailWorks=true;e.Refresh().GetAwaiter().GetResult();
  // Explicitly make the card active so the normal warm-task scheduler retries detail.
  f.Now+=1;f.Status="active";f.TurnStatus="inProgress";e.Refresh().GetAwaiter().GetResult();
  Check(f.Details>=2&&Current(e).Message=="full detail recovered","detail endpoint did not recover after backoff");
 }
 static void MissingMessageIsNotMissingStatus(){
  var f=new Fixture{Text=""};var e=new MonitorEngine(new Settings(),f.Call,()=>f.Now);
  var previous=new Card{Host=Host,Id=Id,TurnId="first",Status="active",Message="last visible message"};e.Cards[previous.Key]=previous;
  e.Refresh().GetAwaiter().GetResult();var c=Current(e);
  Check(!c.Stale&&c.Status=="active"&&c.MessageUnavailable&&c.Message==previous.Message,"missing compact text erased valid status or last content");
 }
 static void RealFailuresRemainStale(){
  foreach(bool wrong in new[]{false,true}){
   var f=new Fixture{BrokenCompact=!wrong,WrongHost=wrong};var e=new MonitorEngine(new Settings(),f.Call,()=>f.Now);e.Refresh().GetAwaiter().GetResult();
   Check(Current(e).Stale,wrong?"another host's snapshot was accepted":"two failing endpoints fabricated a fresh card");
   f.Now+=16;e.Refresh().GetAwaiter().GetResult();Check(f.Details==2,"two failing endpoints delayed the normal full-detail retry");
  }
 }
 static void AttentionFlagsSurviveFallback(){
  foreach(string flag in new[]{"waitingOnApproval","waitingOnUserInput","reconnecting"}){
   var f=new Fixture{Flag=flag};var e=new MonitorEngine(new Settings(),f.Call,()=>f.Now);e.Refresh().GetAwaiter().GetResult();var c=Current(e);
   Check(!c.Stale&&c.Status==(flag=="waitingOnApproval"?"approval":flag=="waitingOnUserInput"?"input":"reconnecting"),"compact active flag lost: "+flag);
  }
 }
 static int Main(){string original=Settings.DataDir;try{Settings.DataDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"compact-test-data-"+Guid.NewGuid().ToString("N"));OversizedTurnKeepsUpdating();MissingMessageIsNotMissingStatus();RealFailuresRemainStale();AttentionFlagsSurviveFallback();Console.WriteLine("PASS: "+checks+" compact fallback, backoff, recovery, status/message separation and host checks");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{Settings.DataDir=original;}}
}
