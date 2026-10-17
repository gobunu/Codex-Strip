using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using CodexStrip;
class ListIsolationTest {
 const string Host="remote-ssh-discovered:fixture";
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static object Detail(string id){return J.Obj("thread",J.Obj("id",id,"hostId",Host,"title",id,"status",J.Obj("type","idle")),"turns",new[]{J.Obj("id","turn-"+id,"status","completed","startedAt",100,"items",new[]{J.Obj("type","agentMessage","text","healthy reply")})});}
 static async Task HangingList(){
  var pending=new TaskCompletionSource<object>();int lists=0,reads=0;double now=100;
  var engine=new MonitorEngine(new Settings(),(tool,args,timeout)=>{
   if(tool=="list_threads"){lists++;return pending.Task;}reads++;return Task.FromResult(Detail(J.Str(args,"threadId")));
  },()=>now);
  var card=new Card{Id="known",Host=Host,Status="active",Stale=true};engine.Cards[card.Key]=card;
  var clock=Stopwatch.StartNew();await engine.Refresh();
  Check(clock.ElapsedMilliseconds<2000&&reads==1&&!engine.Cards[card.Key].Stale,"pending list blocked a healthy known conversation");
  Check(engine.Connected&&!engine.Refreshing,"pending list kept the refresh lock or reported a disconnect");
  now+=31;await engine.Refresh();Check(lists==1&&reads==2,"pending list overlapped another request or stopped visible fallback reads");
  Check(engine.Connection==UiText.T("列表读取较慢 · 任务单独更新","Slow task list · Updating known tasks"),"slow discovery was not identified separately");
  engine.Cards[card.Key].SetUnread(true,J.Now+.01);
  pending.SetResult(J.Obj("threads",new[]{J.Obj("id","new","hostId",Host,"kind","codex","status","active","updatedAt",now),J.Obj("id","known","hostId",Host,"kind","codex","status","idle","updatedAt",now,"isUnread",false)}));
  await engine.Refresh();Check(engine.Cards.ContainsKey(Host+"/new")&&engine.Cards[Host+"/new"].Message=="healthy reply","background list result was not consumed");
  Check(engine.Cards[card.Key].Unread,"late list observation overwrote a newer explicit unread receipt");
 }
 static async Task ListTimeoutDoesNotInvalidateCards(){
  int lists=0,reads=0;double now=100;
  var e=new MonitorEngine(new Settings(),(tool,args,timeout)=>{
   if(tool=="list_threads"){lists++;throw new TimeoutException("list only");}reads++;return Task.FromResult(Detail(J.Str(args,"threadId")));
  },()=>now);
  var card=new Card{Id="healthy",Host=Host,Status="active",Message="cached"};e.Cards[card.Key]=card;
  await e.Refresh();Check(e.Connected&&!e.Cards[card.Key].Stale&&e.Cards[card.Key].Message=="healthy reply","list timeout invalidated a healthy card");
  now+=5;await e.Refresh();Check(lists==1,"list timeout immediately launched another expensive request");
  now+=26;await e.Refresh();Check(lists==2&&reads>=2&&!e.Cards[card.Key].Stale,"list retry did not back off or affected known task freshness");
 }
 static void HostOwnership(){
  const string a="00000000-0001-7000-8000-000000000001",b="00000000-0002-7000-8000-000000000002",c="00000000-0003-7000-8000-000000000003",unknown="00000000-0004-7000-8000-000000000004",extra="00000000-0005-7000-8000-000000000005";
  var data=J.Obj("electron-persisted-atom-state",J.Obj("prompt-history",J.Obj(a,new string[0],b,new string[0],c,new string[0],unknown,new string[0])),"thread-project-membership-host-ids",J.Obj(a,Host),"thread-project-assignments",J.Obj(b,J.Obj("projectKind","remote","hostId",Host)),"thread-workspace-root-hints",J.Obj(c,@"C:\workspace"),"remote-projects",new[]{J.Obj("hostId",Host),J.Obj("hostId","remote-control:other")});
  var known=DesktopStream.KnownThreadCandidates(data);
  Check(known.Length==3&&known.Count(t=>t.Host==Host)==2&&known.Single(t=>t.Id==c).Host=="local","host ownership was not resolved from desktop records");
  Check(known.All(t=>t.Id!=unknown&&t.Host!="remote-control:other"),"unknown thread was guessed across unrelated hosts");
  Check(DesktopStream.KnownThreadCandidates(data,1).Length==1,"discovery limit was ignored");
  ((System.Collections.Generic.Dictionary<string,object>)J.Get(data,"thread-project-membership-host-ids"))[extra]=Host;
  Check(DesktopStream.KnownThreadCandidates(data).Any(t=>t.Id==extra&&t.Host==Host),"known task without prompt history was not discovered");
 }
 static int Main(){string data=Settings.DataDir;try{
  Settings.DataDir=Path.Combine(Path.GetTempPath(),"strip-list-isolation-"+Guid.NewGuid().ToString("N"));
  HangingList().GetAwaiter().GetResult();ListTimeoutDoesNotInvalidateCards().GetAwaiter().GetResult();HostOwnership();
  Console.WriteLine("PASS: "+checks+" hanging list isolation, background completion, backoff, healthy card retention and known-host-only discovery checks");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{Settings.DataDir=data;}}
}
