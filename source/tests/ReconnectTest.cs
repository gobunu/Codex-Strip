using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using CodexStrip;

class ReconnectTest {
 static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
 static object Listed(string id,string host){return J.Obj("id",id,"hostId",host,"kind","codex","title",id,"status","active");}
 static async Task OfflineHost(){
  const string offline="remote-control:offline";int listTimeout=0,listLimit=0,waits=0,reads=0;
  var settings=new Settings{Count=4};settings.Watched["watched"]=offline;
  var engine=new MonitorEngine(settings,(tool,args,timeout)=>{
   if(tool=="list_threads"){listTimeout=timeout;listLimit=(int)J.Num(args,"limit");return Task.FromResult<object>(J.Obj("threads",new[]{Listed("online","remote-test"),Listed("cached",offline)},"unavailableHosts",new[]{J.Obj("hostId",offline)}));}
   if(tool=="wait_threads"){
    var targets=J.Arr(J.Get(args,"targets"));Require(targets.Length==1&&J.Str(targets[0],"hostId")=="remote-test","offline host was polled");waits++;
    return Task.FromResult<object>(J.Obj("polls",new[]{J.Obj("thread",J.Obj("id","online","hostId","remote-test","status",J.Obj("type","active")))}));
   }
   if(tool=="read_thread"){Require(J.Str(args,"hostId")!=offline,"offline host was read");reads++;return Task.FromResult<object>(J.Obj());}
   throw new Exception(tool);
  });
  engine.Cards[offline+"/cached"]=new Card{Id="cached",Host=offline,Title="cached",Message="saved progress",Status="active"};
  await engine.Refresh();
  Require(listTimeout==45000,"list request cannot outlast desktop host enumeration");
  Require(listLimit==20&&engine.Connected&&!engine.Refreshing&&waits==0&&reads==1,"healthy host failed to refresh or used redundant snapshots");
  Require(engine.Cards[offline+"/cached"].Stale&&engine.Cards[offline+"/cached"].Message=="saved progress","offline card lost its content or stale state");
  Require(engine.Cards.ContainsKey(offline+"/watched")&&engine.Cards[offline+"/watched"].Stale,"watched offline card was dropped");
  Console.WriteLine("PASS: 45-second list budget; offline host skipped for snapshots, details and watched tasks; cached content retained");
 }
 static async Task TimeoutRecovery(){
  int mode=0;double now=100;var engine=new MonitorEngine(new Settings(),(tool,args,timeout)=>{
   if(mode==1)throw new TimeoutException("slow desktop");
   if(mode==2)throw new IOException("closed pipe");
   return Task.FromResult<object>(J.Obj("threads",new object[0]));
  },()=>now);
  await engine.Refresh();Require(engine.Connected,"initial refresh failed");
  engine.Cards["remote-test/kept"]=new Card{Host="remote-test",Id="kept",Message="last progress",Status="active"};
  var pipe=typeof(Bridge).GetProperty("PipeName");pipe.GetSetMethod(true).Invoke(engine.Bridge,new object[]{"healthy-pipe"});
  mode=1;await engine.Refresh();
  Require(engine.Connected&&engine.Connection==UiText.T("列表超时 · 任务单独更新","List timed out · Updating known tasks")&&!engine.Refreshing,"timeout reported a disconnect or blocked future refreshes");
  Require(engine.Cards["remote-test/kept"].Message=="last progress"&&engine.Cards["remote-test/kept"].Stale,"timeout discarded the snapshot");
  Require(engine.Bridge.PipeName=="healthy-pipe","request timeout reset a healthy pipe");
  now+=31;mode=0;await engine.Refresh();Require(engine.Connected&&engine.Connection=="已连接 Codex","retry did not recover");
  now+=31;mode=2;await engine.Refresh();Require(!engine.Connected&&engine.Bridge.PipeName==null,"actual disconnect was suppressed");
  var fresh=new MonitorEngine(new Settings(),(tool,args,timeout)=>{throw new TimeoutException();});await fresh.Refresh();Require(!fresh.Connected,"initial timeout invented a prior connection");
  Console.WriteLine("PASS: request timeout preserves connection, pipe and cached content; retry recovers; transport failure still disconnects");
 }
 static async Task PipeTimeout(){
  string name="strip-timeout-test-"+Guid.NewGuid().ToString("N");
  using(var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
   var listening=server.WaitForConnectionAsync();
   var rpc=typeof(Bridge).GetMethod("Rpc",BindingFlags.NonPublic|BindingFlags.Instance);
   var clock=Stopwatch.StartNew();
   var call=(Task<object>)rpc.Invoke(new Bridge(),new object[]{name,"tools/list",J.Obj(),200});
   var first=await Task.WhenAny(listening,call,Task.Delay(3000));
   if(first==call)await call;
   Require(first==listening,"test pipe did not accept its client");await listening;
   try{await call;throw new Exception("silent server did not time out");}catch(TimeoutException){Require(clock.ElapsedMilliseconds<3000,"pipe disposal failed to bound timeout");}
  }
  Console.WriteLine("PASS: silent named-pipe server raises TimeoutException and releases the request");
  var missing=typeof(Bridge).GetMethod("Rpc",BindingFlags.NonPublic|BindingFlags.Instance);
  try{await (Task<object>)missing.Invoke(new Bridge(),new object[]{"strip-missing-test-"+Guid.NewGuid().ToString("N"),"tools/list",J.Obj(),100});throw new Exception("missing server was accepted");}
  catch(IOException){Console.WriteLine("PASS: missing pipe remains a transport failure");}
 }
 static int Main(){string data=Settings.DataDir;try{Settings.DataDir=Path.Combine(Path.GetTempPath(),"strip-reconnect-test-"+Guid.NewGuid().ToString("N"));OfflineHost().GetAwaiter().GetResult();TimeoutRecovery().GetAwaiter().GetResult();PipeTimeout().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{Settings.DataDir=data;}}
}
