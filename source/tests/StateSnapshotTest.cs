using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexStrip;

// The only child protocol implemented here is a disposable test fixture. Every
// file it can open must belong to this test's GUID-named temporary directory.
class StateSnapshotTest {
 const string Prefix="strip-state-snapshot-test-";
 const string Host="remote-ssh-discovered:state-fixture";
 const string ThreadId="00000000-0001-7000-8000-000000000001";
 const int Magic=0x53535231;
 static int checks;
 static string root;
 sealed class Child {public long StartTicks;public string Executable;}
 static readonly Dictionary<int,Child> children=new Dictionary<int,Child>();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
 static extern bool MoveFileEx(string existing,string replacement,int flags);
 sealed class Clock {
  double value=10000;
  public double Read(){return Interlocked.CompareExchange(ref value,0,0);}
  public void Advance(double seconds){Interlocked.Exchange(ref value,Read()+seconds);}
 }
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static void Wait(Func<bool> predicate,int timeoutMs,string message){
  var watch=Stopwatch.StartNew();
  while(!predicate()){if(watch.ElapsedMilliseconds>=timeoutMs)throw new Exception(message);Thread.Sleep(5);}
 }
 static bool IsFixturePath(string path){
  string full=Path.GetFullPath(path),parent=Path.GetDirectoryName(full);
  string name=Path.GetFileName(parent),suffix=name!=null&&name.StartsWith(Prefix,StringComparison.Ordinal)?name.Substring(Prefix.Length):"";
  Guid id;
  return parent!=null&&Path.GetDirectoryName(parent).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)
   &&Guid.TryParseExact(suffix,"N",out id)&&full.EndsWith(".bak",StringComparison.OrdinalIgnoreCase);
 }
 static string PathFor(string name){return Path.Combine(root,name+".json.bak");}
 static string Fixture(int sequence){
  return J.Json(J.Obj("sequence",sequence,"electron-thread-read-state-v1",J.Obj("version",1,"unreadByIdentity",J.Obj("fixture-account",J.Obj(Host+":fixture-scope",new[]{ThreadId}))),
   "electron-persisted-atom-state",J.Obj("remote-thread-summaries-v3:"+Host,new[]{J.Obj("conversationId",ThreadId,"hostId",Host,"title","Fixture conversation","threadRuntimeStatus",J.Obj("type","completed"),"hasUnreadTurn",true,"updatedAt",1234)},"padding",new string('x',128*1024))));
 }
 static void Write(string path,string value){
  if(!IsFixturePath(path))throw new InvalidOperationException("outside fixture directory");
  File.WriteAllText(path,value,new UTF8Encoding(false));
 }
 static void Track(DesktopStateSnapshot snapshot){
  int pid=snapshot.GetDiagnostics().HelperPid;if(pid<=0)return;
  try{using(var process=Process.GetProcessById(pid)){
   var child=new Child{StartTicks=process.StartTime.ToUniversalTime().Ticks,Executable=process.MainModule.FileName};
   lock(children)children[pid]=child;
  }}catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
 }
 static void Refresh(DesktopStateSnapshot snapshot,Clock clock){
  var before=snapshot.GetDiagnostics();clock.Advance(Math.Max(2.1,before.RetrySeconds+.1));snapshot.RequestRefresh();
  Wait(delegate{var current=snapshot.GetDiagnostics();Track(snapshot);return current.Attempts>before.Attempts&&!current.InFlight;},4000,"snapshot request did not complete");
 }
 static object Cached(DesktopStateSnapshot snapshot,int sequence,string message){
  object data;Check(snapshot.TryGetSnapshot(out data)&&J.Num(data,"sequence")==sequence,message);return data;
 }
 static void CheckFields(object data){
  var unread=DesktopStream.ParseDesktopUnread(data,"fixture-account");
  Check(unread.ContainsKey(Host)&&unread[Host].SetEquals(new[]{ThreadId}),"account/host scoped unread data was lost");
  var candidates=DesktopStream.KnownThreadCandidates(data);
  Check(candidates.Length==1&&candidates[0].Id==ThreadId&&candidates[0].Host==Host&&candidates[0].Title=="Fixture conversation"&&candidates[0].Status=="completed","candidate discovery data was lost");
 }
 static void NativeCacheAndBackoff(string executable){
  string path=PathFor("native");Write(path,Fixture(1));var clock=new Clock();
  using(var snapshot=new DesktopStateSnapshot(path,executable,1500,clock.Read)){
   int updates=0;snapshot.Updated+=delegate{Interlocked.Increment(ref updates);};
   Refresh(snapshot,clock);var data=Cached(snapshot,1,"native helper did not publish valid JSON");CheckFields(data);
   Check(snapshot.GetDiagnostics().Successes==1&&snapshot.GetDiagnostics().ConsecutiveFailures==0,"successful native read did not reset diagnostics");
   Check(updates>0,"a published snapshot did not notify its consumer");
   Write(path,"{broken json");Refresh(snapshot,clock);Cached(snapshot,1,"bad JSON replaced the last good cache");
   File.Delete(path);Refresh(snapshot,clock);Cached(snapshot,1,"missing backup removed the last good cache");
   Refresh(snapshot,clock);var failed=snapshot.GetDiagnostics();
   Check(failed.ConsecutiveFailures==3&&failed.RetrySeconds>=4.9,"three consecutive failures did not start the longer backoff");
   var watch=Stopwatch.StartNew();
   for(int i=0;i<1000;i++){snapshot.RequestRefresh();object ignored;snapshot.TryGetSnapshot(out ignored);}
   Check(watch.ElapsedMilliseconds<300,"UI-facing refresh/cache methods blocked during backoff");
   Thread.Sleep(30);Check(snapshot.GetDiagnostics().Attempts==failed.Attempts,"requests bypassed the failure cooldown or were queued individually");
   foreach(double expected in new[]{10.0,20.0,30.0,30.0}){
    Refresh(snapshot,clock);Check(Math.Abs(snapshot.GetDiagnostics().RetrySeconds-expected)<.05,"failure backoff did not increase and remain capped at thirty seconds");
    Cached(snapshot,1,"longer retry backoff invalidated the last good cache");
   }
   Write(path,Fixture(2));Refresh(snapshot,clock);CheckFields(Cached(snapshot,2,"snapshot did not recover after a missing or corrupt backup"));
   Check(snapshot.GetDiagnostics().ConsecutiveFailures==0,"recovery did not clear failure count");
  }
 }
 static void HelperRestartAndBoundedRequests(string self){
  string path=PathFor("healthy");Write(path,Fixture(3));var clock=new Clock();
  using(var snapshot=new DesktopStateSnapshot(path,self,1000,clock.Read)){
   Refresh(snapshot,clock);Cached(snapshot,3,"protocol fixture did not produce a cache");
   int oldPid=snapshot.GetDiagnostics().HelperPid;Check(oldPid>0,"persistent helper PID was not reported");
   using(var process=Process.GetProcessById(oldPid)){process.Kill();Check(process.WaitForExit(1500),"fixture helper did not exit");}
   Write(path,Fixture(4));Refresh(snapshot,clock);
   if(snapshot.GetDiagnostics().Successes<2)Refresh(snapshot,clock);
   Cached(snapshot,4,"helper exit prevented a later refresh from recreating it");
   Check(snapshot.GetDiagnostics().HelperPid>0&&snapshot.GetDiagnostics().HelperPid!=oldPid,"helper process was not replaced after exiting");
   int handles,threads;
   using(var process=Process.GetCurrentProcess()){process.Refresh();handles=process.HandleCount;threads=process.Threads.Count;}
   long attempts=snapshot.GetDiagnostics().Attempts;
   for(int i=0;i<40;i++){
    Refresh(snapshot,clock);var before=snapshot.GetDiagnostics();
    for(int j=0;j<25;j++)snapshot.RequestRefresh();
    Check(snapshot.GetDiagnostics().Attempts==before.Attempts,"repeated immediate requests escaped the minimum refresh interval");
   }
   Check(snapshot.GetDiagnostics().Attempts==attempts+40,"coalesced requests accumulated extra background work");
   using(var process=Process.GetCurrentProcess()){process.Refresh();Check(process.HandleCount<=handles+20,"repeated refreshes leaked process handles");Check(process.Threads.Count<=threads+6,"repeated refreshes kept creating threads");}
  }
 }
 static bool Exited(int pid){try{using(var process=Process.GetProcessById(pid))return process.HasExited;}catch(ArgumentException){return true;}}
 static void TimeoutReleasesWriter(string self){
  string path=PathFor("hang");Write(path,Fixture(5));var clock=new Clock();
  using(var snapshot=new DesktopStateSnapshot(path,self,700,clock.Read)){
   snapshot.RequestRefresh();
   Wait(delegate{Track(snapshot);return File.Exists(path+".holding");},2500,"hanging fixture never opened its temporary target");
   int pid=snapshot.GetDiagnostics().HelperPid;Check(pid>0,"hanging helper PID was not observable");
   string temporary=path+".replacement";File.WriteAllText(temporary,Fixture(6),new UTF8Encoding(false));
   int denied=0,lastError=0;var watch=Stopwatch.StartNew();
   var writer=Task.Factory.StartNew(delegate{
    while(watch.ElapsedMilliseconds<2500){
     if(MoveFileEx(temporary,path,1))return true;
     lastError=Marshal.GetLastWin32Error();Interlocked.Increment(ref denied);Thread.Sleep(5);
    }return false;
   },TaskCreationOptions.LongRunning);
   var ui=Stopwatch.StartNew();
   for(int i=0;i<1000;i++){snapshot.RequestRefresh();object ignored;snapshot.TryGetSnapshot(out ignored);}
   Check(ui.ElapsedMilliseconds<300,"an unresponsive helper blocked the consumer thread");
   Check(writer.Wait(3000)&&writer.Result,"watchdog did not release the fixture handle for target replacement (Win32 "+lastError+")");
   Wait(delegate{return !snapshot.GetDiagnostics().InFlight;},1500,"timed-out request stayed in flight");
   var diagnostics=snapshot.GetDiagnostics();
   Check(denied>0,"fault fixture did not actually reproduce an open-target replacement conflict");
   Check(diagnostics.Timeouts>=1&&diagnostics.ConsecutiveFailures==1&&diagnostics.Attempts==1,"timeout was not recorded once or repeated requests were not merged");
   Check(Exited(pid),"timed-out fixture helper remained alive after watchdog cleanup");
   Check(watch.ElapsedMilliseconds<2500,"watchdog recovery exceeded the bounded test deadline");
  }
 }
 static void MalformedProtocol(string self){
  string path=PathFor("protocol");Write(path,Fixture(7));var clock=new Clock();
  using(var snapshot=new DesktopStateSnapshot(path,self,700,clock.Read)){
   Refresh(snapshot,clock);object ignored;
   Check(!snapshot.TryGetSnapshot(out ignored),"invalid helper frame was accepted as a snapshot");
   Check(snapshot.GetDiagnostics().ConsecutiveFailures==1&&!snapshot.GetDiagnostics().InFlight,"invalid helper frame left the reader stuck");
  }
 }
 static void SlowStartupDeadline(string self){
  string path=PathFor("slowstart");Write(path,Fixture(8));var clock=new Clock();
  using(var snapshot=new DesktopStateSnapshot(path,self,200,clock.Read)){
   var watch=Stopwatch.StartNew();snapshot.RequestRefresh();int pid=0;
   Wait(delegate{Track(snapshot);int current=snapshot.GetDiagnostics().HelperPid;if(current>0)pid=current;return !snapshot.GetDiagnostics().InFlight;},2000,"slow-start helper escaped the request deadline");
   var diagnostics=snapshot.GetDiagnostics();object ignored;
   Check(File.Exists(path+".starting"),"slow-start fixture was not reached before the deadline");
   Check(diagnostics.Timeouts==1&&diagnostics.ConsecutiveFailures==1&&diagnostics.Successes==0,"deadline excluded the helper's delayed initialization");
   Check(!snapshot.TryGetSnapshot(out ignored),"a reply arriving after the deadline was published");
   Check(pid>0&&Exited(pid)&&diagnostics.HelperPid==0,"slow-start helper was not reaped after timeout");
   Check(watch.ElapsedMilliseconds<1500,"slow-start timeout did not converge within a bounded period");
  }
 }
 static void DisposeInFlight(string self){
  string path=PathFor("dispose");Write(path,Fixture(9));var clock=new Clock();
  var snapshot=new DesktopStateSnapshot(path,self,1500,clock.Read);int updates=0;
  try{
   snapshot.Updated+=delegate{Interlocked.Increment(ref updates);};snapshot.RequestRefresh();
   Wait(delegate{Track(snapshot);return File.Exists(path+".reading");},1000,"dispose fixture did not enter its delayed READ");
   int pid=snapshot.GetDiagnostics().HelperPid;Check(pid>0&&snapshot.GetDiagnostics().InFlight,"fixture was not in flight before Dispose");
   var watch=Stopwatch.StartNew();snapshot.Dispose();
   Check(watch.ElapsedMilliseconds<1000,"Dispose blocked on an unresponsive helper");
   Wait(delegate{return !snapshot.GetDiagnostics().InFlight;},1500,"disposed request did not leave its in-flight state");
   object ignored;var diagnostics=snapshot.GetDiagnostics();
   Check(updates==0&&!snapshot.TryGetSnapshot(out ignored)&&diagnostics.Successes==0,"an in-flight operation published data or Updated after Dispose");
   Check(diagnostics.HelperPid==0&&Exited(pid),"Dispose did not reap its in-flight helper");
   long attempts=diagnostics.Attempts;for(int i=0;i<100;i++)snapshot.RequestRefresh();Thread.Sleep(30);
   Check(snapshot.GetDiagnostics().Attempts==attempts,"Dispose allowed new background requests");
  }finally{snapshot.Dispose();}
 }
 static void RejectNonBackupPaths(string self){
  bool rejected=false;
  try{using(var snapshot=new DesktopStateSnapshot(Path.Combine(root,"main.json"),self)){} }catch(ArgumentException){rejected=true;}
  Check(rejected,"a main-file path was accepted by the backup-only reader");rejected=false;
  try{using(var snapshot=new DesktopStateSnapshot("relative.json.bak",self)){} }catch(ArgumentException){rejected=true;}
  Check(rejected,"a relative reader path was accepted");
 }
 static int FakeHelper(string[] args){
  if(args.Length!=4)return 91;
  string path=Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));if(!IsFixturePath(path))return 92;
  using(var lifetime=new Timer(delegate{Environment.Exit(93);},null,25000,Timeout.Infinite))
  using(var output=new BinaryWriter(Console.OpenStandardOutput())){
   if(Path.GetFileName(path).StartsWith("slowstart.",StringComparison.Ordinal)){
    File.WriteAllText(path+".starting","starting");Thread.Sleep(500);
   }
   string command;
   while((command=Console.ReadLine())!=null){
    if(command=="EXIT")return 0;if(command!="READ")return 94;
    if(Path.GetFileName(path).StartsWith("dispose.",StringComparison.Ordinal)){
     File.WriteAllText(path+".reading","reading");Thread.Sleep(500);
    }
    if(Path.GetFileName(path).StartsWith("hang.",StringComparison.Ordinal)){
     using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
      File.WriteAllText(path+".holding","holding");Thread.Sleep(Timeout.Infinite);
     }
    }
    if(Path.GetFileName(path).StartsWith("protocol.",StringComparison.Ordinal)){
     output.Write(1234);output.Write(0);output.Write(0);output.Write(0);output.Write(0L);output.Flush();continue;
    }
    byte[] bytes=null;int status=0,error=0;long ticks=0;
    try{bytes=File.ReadAllBytes(path);ticks=File.GetLastWriteTimeUtc(path).Ticks;}catch(FileNotFoundException){status=1;}catch(Exception exception){status=4;error=exception.HResult&65535;}
    output.Write(Magic);output.Write(status);output.Write(error);output.Write(bytes==null?0:bytes.Length);output.Write(ticks);if(bytes!=null)output.Write(bytes);output.Flush();
   }
  }return 0;
 }
 static void Cleanup(){
  lock(children)foreach(var entry in children)try{using(var process=Process.GetProcessById(entry.Key)){
   // Verify ownership before a late cleanup; never act on a reused PID.
   if(process.StartTime.ToUniversalTime().Ticks==entry.Value.StartTicks&&process.MainModule.FileName.Equals(entry.Value.Executable,StringComparison.OrdinalIgnoreCase)){process.Kill();process.WaitForExit(1000);}
  }}catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
  if(root!=null&&IsFixturePath(Path.Combine(root,"cleanup.json.bak"))&&Directory.Exists(root))Directory.Delete(root,true);
 }
 static int Main(string[] args){
  if(args.Length>0&&args[0]=="--state-reader")return FakeHelper(args);
  try{
   if(args.Length!=1||!File.Exists(args[0]))throw new ArgumentException("Pass the freshly built CodexStrip.exe as the only argument.");
   root=Path.Combine(Path.GetTempPath(),Prefix+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   RejectNonBackupPaths(typeof(StateSnapshotTest).Assembly.Location);
   NativeCacheAndBackoff(Path.GetFullPath(args[0]));
   HelperRestartAndBoundedRequests(typeof(StateSnapshotTest).Assembly.Location);
   TimeoutReleasesWriter(typeof(StateSnapshotTest).Assembly.Location);
   MalformedProtocol(typeof(StateSnapshotTest).Assembly.Location);
   SlowStartupDeadline(typeof(StateSnapshotTest).Assembly.Location);
   DisposeInFlight(typeof(StateSnapshotTest).Assembly.Location);
   Console.WriteLine("PASS: "+checks+" snapshot field retention, good-cache preservation, retry backoff, nonblocking/coalesced refresh, bounded resources, helper restart and watchdog handle-release checks");return 0;
  }catch(Exception exception){Console.Error.WriteLine(exception);return 1;}finally{Cleanup();}
 }
}
