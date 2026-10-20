using System;
using System.Threading.Tasks;
using System.Web;
using CodexStrip;

class ThreadNavigationTest {
 static void Require(bool value,string message){if(!value)throw new Exception(message);}
 static int Main(){try{
  const string id="00000000-0000-7000-8000-000000000001";
  const string childId="00000000-0000-7000-8000-000000000002",middleId="00000000-0000-7000-8000-000000000003";
  var main=new Card{Id=id,Host="remote-ssh-discovered:example-host"};
  var middle=new Card{Id=middleId,Host=main.Host,Ephemeral=true,ParentId=id};
  var child=StreamState.Project(J.Obj("ephemeral",true,"parentThreadId",middleId),main.Host,childId);
  Require(ThreadNavigation.Resolve(child,new[]{main,middle,child})==main,"nested temporary thread did not reach main");
  child=StreamState.Project(J.Obj("ephemeral",true,"source",J.Obj("subAgent",J.Obj("thread_spawn",J.Obj("parent_thread_id",id)))),main.Host,childId);
  Require(ThreadNavigation.Resolve(child,new[]{main,child})==main,"source parent lost");
  child=StreamState.Project(J.Obj("sideConversation",true,"sideConversationParentNavigationPath","/local/"+id+"?hostId=remote-ssh-discovered%3Aexample-host"),"local",childId);
  Require(ThreadNavigation.Resolve(child,new[]{main,child})==main,"side conversation lost parent host");
  child=new Card{Id=childId,Host=main.Host,Ephemeral=true};
  main.ChildMetadata(new[]{J.Obj("type","subAgentActivity","agentThreadId",childId)});
  Require(ThreadNavigation.Resolve(child,new[]{main,child})==main,"parent activity did not resolve ended child");
  var retained=MonitorEngine.PreferFresh(child,new Card{Id=childId,Host=main.Host});
  Require(retained.Ephemeral,"temporary metadata lost on refresh");
  Require(ThreadNavigation.Resolve(main,new[]{main})==main,"normal navigation changed");
  foreach(var broken in new[]{new Card{Id=childId,Host=main.Host,Ephemeral=true},new Card{Id=childId,Host=main.Host,ParentId=childId}}){
   bool rejected=false;try{ThreadNavigation.Resolve(broken,new[]{broken});}catch(System.IO.IOException){rejected=true;}
   Require(rejected,"orphan or cycle attempted restore");
  }
  int calls=0,launches=0;string link=null;
  Func<string,object,int,Task<object>> call=(tool,args,timeout)=>{
   calls++;Require(tool=="navigate_to_codex_page"&&J.Str(args,"threadId")==id,"local navigation request changed");
   return Task.FromResult<object>(J.Obj("navigated",true));
  };
  Action<string> launch=value=>{launches++;link=value;};
  var redirected=ThreadNavigation.Resolve(child,new[]{main,child});
  ThreadNavigation.Open(redirected,call,launch).GetAwaiter().GetResult();
  Require(link==ThreadNavigation.DesktopLink(main),"temporary navigation launched child instead of main");
  calls=0;launches=0;link=null;
  ThreadNavigation.Open(new Card{Host="local",Id=id},call,launch).GetAwaiter().GetResult();
  Require(calls==1&&launches==0,"local thread did not use desktop tool");
  foreach(string host in new[]{"durable","remote-ssh-discovered:example-host","remote-control:env_example","remote-ssh-discovered:example host&view=other#fragment"}){
   ThreadNavigation.Open(new Card{Host=host,Id=id},call,launch).GetAwaiter().GetResult();
   var uri=new Uri(link);var query=HttpUtility.ParseQueryString(uri.Query);
   Require(uri.Scheme=="codex"&&uri.Host=="threads"&&uri.AbsolutePath=="/"+id,"wrong native navigation route");
   Require(query.Count==1&&query["hostId"]==host&&uri.Fragment.Length==0,"host lost or changed URL structure");
  }
  Require(calls==1&&launches==4,"hosted navigation performed cross-host discovery");
  var stalled=new TaskCompletionSource<object>();
  var opening=ThreadNavigation.Open(new Card{Host="durable",Id=id},(tool,args,timeout)=>{calls++;return stalled.Task;},launch);
  Require(opening.IsCompleted,"hosted navigation waited for a stalled native navigator");
  opening.GetAwaiter().GetResult();
  Require(calls==1&&launches==5,"hosted navigation left a native request that could switch pages later");
  ThreadNavigation.Open(new Card{Host="remote-control:env_example",Id=id},(tool,args,timeout)=>{throw new System.IO.IOException("disconnected app tools");},launch).GetAwaiter().GetResult();
  Require(launches==6,"known-host navigation depends on app-tools availability");
  string currentExecutable=@"C:\Program Files\WindowsApps\CurrentDesktop\ChatGPT.exe";
  var direct=ThreadNavigation.DesktopLaunchInfo(link,currentExecutable);
  Require(direct.FileName==currentExecutable&&direct.Arguments=="\""+link+"\""&&direct.UseShellExecute,"hosted navigation did not use the running desktop executable with one quoted URL");
  Require(direct.WindowStyle==System.Diagnostics.ProcessWindowStyle.Hidden,"transient desktop launcher became visible");
  var registered=ThreadNavigation.DesktopLaunchInfo(link,null);
  Require(registered.FileName==link&&registered.Arguments.Length==0&&registered.UseShellExecute,"desktop-not-running protocol fallback changed");
  foreach(var bad in new[]{new Card{Host="durable",Id="../settings"},new Card{Host="durable\n",Id=id}}){
   bool rejected=false;try{ThreadNavigation.Open(bad,call,launch).GetAwaiter().GetResult();}catch(ArgumentException){rejected=true;}
   Require(rejected,"invalid link was launched");
  }
  Require(calls==1&&launches==6,"invalid metadata triggered navigation");
  bool failed=false;try{ThreadNavigation.Open(new Card{Host="durable",Id=id},call,value=>{throw new InvalidOperationException("launch failed");}).GetAwaiter().GetResult();}catch(InvalidOperationException){failed=true;}
  Require(failed&&calls==1,"failed hosted launch was hidden or sent an unscoped navigation");
  failed=false;try{ThreadNavigation.Open(new Card{Host="local",Id=id},(tool,args,timeout)=>{throw new InvalidOperationException("tool failed");},launch).GetAwaiter().GetResult();}catch(InvalidOperationException){failed=true;}
  Require(failed&&launches==6,"failed local navigation was hidden");
  Console.WriteLine("PASS: local desktop tool; immediate cloud/SSH/remote-control routing despite stalled discovery; URL encoding; invalid metadata; launch/tool failures");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
