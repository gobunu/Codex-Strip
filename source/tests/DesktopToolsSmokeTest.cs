using System;
using System.IO;
using System.Linq;
using CodexStrip;

class DesktopToolsSmokeTest {
 static int Main(string[] args) { try {
  // Match a normal Explorer launch rather than inheriting the agent's pipe.
  Environment.SetEnvironmentVariable("CODEX_THREAD_ID",null);
  Environment.SetEnvironmentVariable("CODEX_APP_TOOLS_PIPE_PATH",null);
  var bridge=new Bridge();
  var list=bridge.Call("list_threads",J.Obj("limit",4)).GetAwaiter().GetResult();
  var threads=J.Arr(J.Get(list,"threads")).Concat(J.Arr(J.Get(list,"pinnedThreads"))).ToArray();
  Console.WriteLine("Discovery=passed; ThreadCount="+threads.Length+"; Pipe="+bridge.PipeName);
  var target=threads.FirstOrDefault(t=>J.Str(t,"hostId")=="local"&&J.Str(t,"id")!=bridge.ContextId);
  if(target==null)throw new Exception("No local validation target");
  string id=J.Str(target,"id");
  var wait=bridge.Call("wait_threads",J.Obj("targets",new[]{J.Obj("threadId",id,"hostId","local")},"timeoutMs",0),15000).GetAwaiter().GetResult();
  if(J.Arr(J.Get(wait,"errors")).Length>0)throw new Exception("Snapshot reported errors");
  Console.WriteLine("ThreadSnapshot=passed");
  var detail=bridge.Call("read_thread",J.Obj("threadId",id,"hostId","local","turnLimit",1,"includeOutputs",false,"maxOutputCharsPerItem",200)).GetAwaiter().GetResult();
  Console.WriteLine("ThreadRead=passed; DetailKeys="+string.Join(",",((System.Collections.Generic.Dictionary<string,object>)detail).Keys));
  var usage=new Usage();usage.Update(bridge.Call("get_usage_limits",J.Obj()).GetAwaiter().GetResult());
  Console.WriteLine("Usage=passed; WeeklyAvailable="+usage.WeeklyRemaining.HasValue);
  if(args.Length>0){
   string originalData=Settings.DataDir;
   try{
    Settings.DataDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"navigation-test-data");
    var card=new Card{Id=args[0],Host=args.Length>1?args[1]:"local",Status="completed",TurnId="sample-turn",CompletionPending=true};
    if(args.Length>2){card.Ephemeral=true;card.ParentId=args[2];card.ParentHost=card.Host;}
    var settings=new Settings();settings.PendingCompletions[card.Key]=card.TurnId;
    bool nativeAcknowledged=false;
    var engine=new MonitorEngine(settings,async (tool,arguments,timeout)=>{var result=await bridge.Call(tool,arguments,timeout);if(tool=="navigate_to_codex_page")nativeAcknowledged=object.Equals(J.Get(result,"navigated"),true);return result;});engine.Cards[card.Key]=card;
    engine.Open(card).GetAwaiter().GetResult();
    if(!nativeAcknowledged)throw new Exception("Current desktop did not acknowledge native navigation");
    Console.WriteLine("NativeNavigationAcknowledged=true; Host="+card.Host);
    if(!settings.Clicks.ContainsKey(card.Key))throw new Exception("Navigation did not record the click");
    if(ThreadNavigation.UsesDesktopLink(card)){
     if(!settings.PendingCompletions.ContainsKey(card.Key)||!card.CompletionPending)throw new Exception("Hosted shell launch prematurely acknowledged completion");
     var read=card.Copy();read.SetUnread(false,J.Now);MonitorEngine.ReconcileCompletion(settings,card,read);
     if(settings.PendingCompletions.ContainsKey(card.Key)||read.UnreadResult)throw new Exception("Desktop read receipt did not acknowledge completion");
    }else if(settings.PendingCompletions.ContainsKey(card.Key)||card.CompletionPending)throw new Exception("Local navigation did not acknowledge completion");
    Console.WriteLine("NavigationRequest=accepted; Host="+card.Host+"; ForegroundActivation=passed; CompletionAcknowledgement=passed");
   }finally{Settings.DataDir=originalData;}
  }
  Console.WriteLine("AllPassed=true");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;} }
}
