using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodexStrip;
class SelectiveSyncTest {
 const string Host="remote-ssh-discovered:fixture";
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 sealed class Fixture {
  public double Now=100,Updated=10;public string Status="idle";public bool Unread;public string[] Ids={"one","two"};
  public int Lists,Reads,Limit;public Func<string,Task<object>> Read;
  public object Detail(string id){return J.Obj("thread",J.Obj("id",id,"hostId",Host,"title",id,"status",J.Obj("type",Status)),"turns",new[]{J.Obj("id","turn-"+id,"status",Status=="active"?"inProgress":"completed","startedAt",Updated,"items",new[]{J.Obj("type","agentMessage","text","reply-"+id)})});}
  public Task<object> Call(string tool,object args,int timeout){
   if(tool=="list_threads"){Lists++;Limit=(int)J.Num(args,"limit");return Task.FromResult<object>(J.Obj("threads",Ids.Select(id=>J.Obj("id",id,"hostId",Host,"kind","codex","title",id,"status",Status,"isUnread",Unread,"updatedAt",Updated)).ToArray()));}
   Check(tool=="read_thread","refresh used redundant snapshot calls");Reads++;string key=J.Str(args,"threadId");return Read==null?Task.FromResult(Detail(key)):Read(key);
  }
  public MonitorEngine Engine(){return new MonitorEngine(new Settings{Count=2},Call,()=>Now);}
 }
 static void ColdAndReactivation(){
  var f=new Fixture();var e=f.Engine();e.Refresh().GetAwaiter().GetResult();Check(f.Limit==20&&f.Reads==2,"recent discovery or first reads failed");
  f.Now+=10;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==4,"warm conversations were not refreshed");
  f.Now+=21;e.Refresh().GetAwaiter().GetResult();Check(f.Lists==3&&f.Reads==4,"unchanged ended conversations did not leave the detail queue");
  Check(e.Cards.Values.All(c=>!c.Stale&&c.Status=="completed"),"cold cards were removed or marked disconnected");
  f.Updated++;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==6,"updated timestamp did not reactivate cold conversations");
  f.Now+=31;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==6,"reactivated conversations did not become cold again");
  f.Unread=true;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==8&&e.Cards.Values.All(c=>c.UnreadResult),"unread change did not reactivate conversations");
  f.Now+=31;f.Status="active";e.Refresh().GetAwaiter().GetResult();Check(f.Reads==10,"status change did not reactivate conversations");
  f.Now+=120;f.Ids=new string[0];e.Refresh().GetAwaiter().GetResult();Check(f.Reads==12&&e.Cards.Count==2,"running tasks outside the recent list stopped being monitored");
  f.Status="idle";e.Refresh().GetAwaiter().GetResult();int ended=f.Reads;f.Now+=31;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==ended,"ended tasks missing from the list kept being read");
  f.Ids=new[]{"one"};f.Updated++;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==ended+1,"a cold conversation returning to recency was not rediscovered");
 }
 static async Task ProgressiveAndBounded(){
  var f=new Fixture{Ids=Enumerable.Range(0,6).Select(i=>"task"+i).ToArray()};var gates=f.Ids.ToDictionary(id=>id,id=>new TaskCompletionSource<object>());
  int running=0,max=0;var entered=new HashSet<string>();
  f.Read=async id=>{running++;max=Math.Max(max,running);entered.Add(id);try{return await gates[id].Task;}finally{running--;}};
  var e=f.Engine();var old=new Card{Id="task0",Host=Host,Title="old",Status="completed",Message="old reply"};e.Cards[old.Key]=old;
  var refresh=e.Refresh();Check(!refresh.IsCompleted&&entered.Count==4&&max==4,"detail concurrency was not bounded at four");
  string fast=entered.First(id=>id!="task0");gates[fast].SetResult(f.Detail(fast));
  for(int i=0;i<100&&!e.Cards.ContainsKey(Host+"/"+fast);i++)await Task.Delay(5);
  Check(e.Cards.ContainsKey(Host+"/"+fast)&&e.Cards[Host+"/"+fast].Message=="reply-"+fast&&!refresh.IsCompleted,"a slow conversation blocked a healthy card from appearing");
  Check(old.Title=="old"&&old.Message=="old reply","published cards were mutated in place");
  foreach(var id in f.Ids)gates[id].TrySetResult(f.Detail(id));await refresh;
  Check(e.Connected&&!e.Refreshing&&e.Cards.Count==6&&max==4,"parallel reads failed to finish cleanly");
 }
 static void FailureBackoff(){
  var f=new Fixture{Ids=new[]{"good","bad"}};int bad=0;
  f.Read=id=>{if(id=="bad"){bad++;throw new IOException("unavailable target");}return Task.FromResult(f.Detail(id));};
  var e=f.Engine();e.Refresh().GetAwaiter().GetResult();
  Check(e.Connected&&!e.Cards[Host+"/good"].Stale&&e.Cards[Host+"/bad"].Stale,"one failed conversation disconnected its healthy peer");
  f.Now+=5;e.Refresh().GetAwaiter().GetResult();Check(bad==1,"failed conversation retried without backoff");
  f.Now+=11;e.Refresh().GetAwaiter().GetResult();Check(bad==2,"failed conversation was never retried");
  f.Read=null;f.Updated++;e.Refresh().GetAwaiter().GetResult();Check(e.Cards.Values.All(c=>!c.Stale),"a changed conversation could not recover during backoff");
 }
 static void EmptyConversation(){
  var c=new Card{Id="empty",Host=Host,Stale=true,Message="last reply"};
  c.Detail(J.Obj("thread",J.Obj("id",c.Id,"hostId",Host,"status",J.Obj("type","notLoaded")),"turns",new object[0]));
  Check(!c.Stale&&c.Status=="notLoaded"&&c.Message=="last reply"&&c.MessageUnavailable,"a readable empty conversation remained stuck waiting to sync");
 }
 static void AgeWindowAndSettings(){
  var f=new Fixture{Now=1000000,Updated=1000000-3*86400};var e=f.Engine();e.Settings.SyncRecentLimit=10;e.Settings.SyncRecentHours=24;
  e.Refresh().GetAwaiter().GetResult();Check(f.Limit==10&&f.Reads==0&&e.Cards.Count==2,"old ended tasks were read to fill the requested count");
  f.Status="active";e.Refresh().GetAwaiter().GetResult();Check(f.Reads==2,"an old running task was excluded by the age window");
  f.Status="idle";e.Refresh().GetAwaiter().GetResult();int ended=f.Reads;
  f.Unread=true;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==ended+2,"old task unread change was lost");
  f.Now+=31;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==ended+2,"old unchanged unread result was polled forever");
  e.Settings.SyncRecentHours=0;e.Refresh().GetAwaiter().GetResult();Check(f.Reads==ended+4,"expanding the window failed to inspect newly eligible conversations");
  Directory.CreateDirectory(Settings.DataDir);File.WriteAllText(Path.Combine(Settings.DataDir,"settings.json"),J.Json(J.Obj("count",4)));
  var legacy=Settings.Load();Check(legacy.SyncRecentLimit==20&&legacy.SyncRecentHours==24,"legacy settings did not receive synchronization defaults");
  legacy.SyncRecentLimit=30;legacy.SyncRecentHours=6;legacy.Save();var saved=Settings.Load();Check(saved.SyncRecentLimit==30&&saved.SyncRecentHours==6,"sync settings did not persist");
  File.WriteAllText(Path.Combine(Settings.DataDir,"settings.json"),J.Json(J.Obj("syncRecentLimit",1000,"syncRecentHours",-1)));saved=Settings.Load();
  Check(saved.SyncRecentLimit==20&&saved.SyncRecentHours==24,"invalid settings escaped validation");
 }
 static int Main(){string data=Settings.DataDir;try{
  Settings.DataDir=Path.Combine(Path.GetTempPath(),"strip-selective-test-"+Guid.NewGuid().ToString("N"));
  ColdAndReactivation();ProgressiveAndBounded().GetAwaiter().GetResult();FailureBackoff();EmptyConversation();AgeWindowAndSettings();
  Console.WriteLine("PASS: "+checks+" recent discovery, cold queue eviction/reactivation, running retention, progressive updates, bounded concurrency, retry and empty conversation checks");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{Settings.DataDir=data;}}
}
