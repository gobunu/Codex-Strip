using System;
using System.Threading.Tasks;
using System.Web;
using CodexStrip;

class ThreadNavigationTest {
 static void Require(bool value,string message){if(!value)throw new Exception(message);}
 static int Main(){try{
  const string id="00000000-0000-7000-8000-000000000001";
  int calls=0,launches=0;string link=null;
  Func<string,object,int,Task<object>> call=(tool,args,timeout)=>{
   calls++;Require(tool=="navigate_to_codex_page"&&J.Str(args,"threadId")==id,"local navigation request changed");
   return Task.FromResult<object>(J.Obj());
  };
  Action<string> launch=value=>{launches++;link=value;};
  ThreadNavigation.Open(new Card{Host="local",Id=id},call,launch).GetAwaiter().GetResult();
  Require(calls==1&&launches==0,"local thread did not use desktop tool");
  foreach(string host in new[]{"durable","remote-ssh-discovered:example-host","remote-ssh-discovered:example host&view=other#fragment"}){
   ThreadNavigation.Open(new Card{Host=host,Id=id},call,launch).GetAwaiter().GetResult();
   var uri=new Uri(link);var query=HttpUtility.ParseQueryString(uri.Query);
   Require(uri.Scheme=="codex"&&uri.Host=="threads"&&uri.AbsolutePath=="/"+id,"wrong native navigation route");
   Require(query.Count==1&&query["hostId"]==host&&uri.Fragment.Length==0,"host lost or changed URL structure");
  }
  Require(calls==1&&launches==3,"hosted thread fell back to local restoration");
  foreach(var bad in new[]{new Card{Host="durable",Id="../settings"},new Card{Host="durable\n",Id=id}}){
   bool rejected=false;try{ThreadNavigation.Open(bad,call,launch).GetAwaiter().GetResult();}catch(ArgumentException){rejected=true;}
   Require(rejected,"invalid link was launched");
  }
  Require(calls==1&&launches==3,"invalid metadata triggered navigation");
  bool failed=false;try{ThreadNavigation.Open(new Card{Host="durable",Id=id},call,value=>{throw new InvalidOperationException("launch failed");}).GetAwaiter().GetResult();}catch(InvalidOperationException){failed=true;}
  Require(failed&&calls==1,"failed hosted launch was silently restored locally");
  failed=false;try{ThreadNavigation.Open(new Card{Host="local",Id=id},(tool,args,timeout)=>{throw new InvalidOperationException("tool failed");},launch).GetAwaiter().GetResult();}catch(InvalidOperationException){failed=true;}
  Require(failed&&launches==3,"failed local navigation was hidden");
  Console.WriteLine("PASS: local desktop tool; cloud and SSH host routing; URL encoding; invalid metadata; launch/tool failures");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
