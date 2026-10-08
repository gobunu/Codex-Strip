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
  if(args.Length>0){var nav=bridge.Call("navigate_to_codex_page",J.Obj("threadId",args[0])).GetAwaiter().GetResult();Console.WriteLine("Navigation="+J.Json(nav));}
  Console.WriteLine("AllPassed=true");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;} }
}
