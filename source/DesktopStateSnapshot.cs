using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CodexStrip {
 public sealed class SnapshotDiagnostics {
  public string Status="waiting";public int ConsecutiveFailures,HelperPid;public long Attempts,Successes,Timeouts;
  public bool InFlight;public double RetrySeconds,LastSuccessUnix;
 }

 // All callers share an immutable, last-good snapshot. No UI call performs file I/O.
 public sealed class DesktopStateSnapshot:IDisposable {
  const int Magic=0x53535231,MaxBytes=64*1024*1024;
  static readonly Lazy<DesktopStateSnapshot> shared=new Lazy<DesktopStateSnapshot>(()=>new DesktopStateSnapshot(
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex",".codex-global-state.json.bak")));
  public static DesktopStateSnapshot Shared {get{return shared.Value;}}
  public static void ShutdownShared(){if(shared.IsValueCreated)shared.Value.Dispose();}
  readonly object gate=new object();readonly string backupPath,executable;readonly int timeoutMs;readonly Func<double> clock;
  object snapshot;byte[] snapshotHash;Helper helper;bool disposed,inFlight;double nextAttempt,lastSuccess;
  long attempts,successes,timeouts;int failures;string status="waiting";
  public event Action Updated;

  public DesktopStateSnapshot(string backupPath,string helperExecutable=null,int requestTimeoutMs=1000,Func<double> clock=null){
   if(string.IsNullOrWhiteSpace(backupPath)||!Path.IsPathRooted(backupPath)||!backupPath.EndsWith(".bak",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("A backup file is required");
   this.backupPath=Path.GetFullPath(backupPath);executable=Path.GetFullPath(helperExecutable??typeof(DesktopStateSnapshot).Assembly.Location);
   timeoutMs=Math.Max(100,Math.Min(10000,requestTimeoutMs));this.clock=clock??(()=>J.Now);
   AppDomain.CurrentDomain.ProcessExit+=OnProcessExit;
  }
  void OnProcessExit(object sender,EventArgs args){Dispose();}
  public bool TryGetSnapshot(out object data){lock(gate){data=snapshot;return data!=null;}}
  public SnapshotDiagnostics GetDiagnostics(){lock(gate){return new SnapshotDiagnostics{Status=status,ConsecutiveFailures=failures,
   HelperPid=helper==null?0:helper.Id,Attempts=attempts,Successes=successes,Timeouts=timeouts,InFlight=inFlight,
   RetrySeconds=Math.Max(0,nextAttempt-clock()),LastSuccessUnix=lastSuccess};}}
  public void RequestRefresh(){lock(gate){if(disposed||inFlight||clock()<nextAttempt)return;inFlight=true;attempts++;}
   // The entire operation, including startup, parsing and callbacks, stays off the caller.
   Task.Run((Func<Task>)Refresh);
  }
  sealed class Reply {public int Status,Error;public long WriteTicks;public byte[] Bytes;}
  sealed class Operation {public bool Cancelled;public Helper Active;}
  sealed class Candidate {public Reply Reply;public byte[] Hash;public object Data;public bool Same;}
  Candidate Collect(Operation operation){
   var active=EnsureHelper(operation);Reply reply=Exchange(active);
   if(reply.Status!=0)return new Candidate{Reply=reply};
   byte[] hash;using(var digest=SHA256.Create())hash=digest.ComputeHash(reply.Bytes);
   bool same;lock(gate)same=Equal(snapshotHash,hash);
   object next=null;if(!same){
    string text;using(var memory=new MemoryStream(reply.Bytes,false))using(var reader=new StreamReader(memory,Encoding.UTF8,true))text=reader.ReadToEnd();
    next=J.Parse(text);if(!(next is Dictionary<string,object>))throw new InvalidDataException("Invalid snapshot root");
   }
   return new Candidate{Reply=reply,Hash=hash,Data=next,Same=same};
  }
  async Task Refresh(){var operation=new Operation();bool changed=false;
   try{
    // The deadline starts before process startup and also covers decoding/parsing.
    var exchange=Task.Run(()=>Collect(operation));
    if(await Task.WhenAny(exchange,Task.Delay(timeoutMs))!=exchange){
     Helper active;lock(gate){timeouts++;operation.Cancelled=true;active=operation.Active;}
     if(active!=null)Retire(active);
     // Closing the owned job and pipes must unblock its reader. Never pile up stuck I/O tasks.
     if(await Task.WhenAny(exchange,Task.Delay(1000))!=exchange){lock(gate){disposed=true;status="reader-stop-timeout";}return;}
     try{await exchange;}catch{}
     throw new TimeoutException();
    }
    Candidate candidate=await exchange;Reply reply=candidate.Reply;
    if(reply.Status!=0){Fail(reply.Status==1?"backup-missing":reply.Status==2?"backup-busy":reply.Status==3?"read-interrupted":"backup-unavailable");return;}
    lock(gate){if(disposed)return;if(!candidate.Same){snapshot=candidate.Data;snapshotHash=candidate.Hash;changed=true;}
     successes++;failures=0;lastSuccess=clock();nextAttempt=lastSuccess+2;status="ready";
    }
   }catch(TimeoutException){Fail("reader-timeout");}
   catch{if(operation.Active!=null)Retire(operation.Active);Fail("snapshot-unavailable");}
   finally{lock(gate)inFlight=false;}
   if(changed){var callbacks=Updated;if(callbacks!=null)foreach(Action callback in callbacks.GetInvocationList()){lock(gate){if(disposed)return;}try{callback();}catch{}}}
  }
  static bool Equal(byte[] a,byte[] b){if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
  void Fail(string reason){lock(gate){if(disposed)return;failures++;status=reason;
   double delay=failures<3?2:Math.Min(30,5*Math.Pow(2,Math.Min(4,failures-3)));nextAttempt=clock()+delay;
  }}
  Helper EnsureHelper(Operation operation){Helper current;lock(gate){if(disposed||operation.Cancelled)throw new ObjectDisposedException("DesktopStateSnapshot");current=helper;}
   if(current!=null&&!current.Exited){lock(gate){if(disposed||operation.Cancelled)throw new ObjectDisposedException("DesktopStateSnapshot");operation.Active=current;}return current;}
   if(current!=null)Retire(current);
   var created=Helper.Start(executable,backupPath);
   bool reject;lock(gate){reject=disposed||operation.Cancelled;if(!reject){helper=created;operation.Active=created;}}
   if(reject){created.Dispose();throw new ObjectDisposedException("DesktopStateSnapshot");}return created;
  }
  void Retire(Helper value){lock(gate){if(helper==value)helper=null;}value.Dispose();}
  static Reply Exchange(Helper active){
   active.Process.StandardInput.WriteLine("READ");active.Process.StandardInput.Flush();
   var reader=new BinaryReader(active.Process.StandardOutput.BaseStream,Encoding.UTF8,true);
   if(reader.ReadInt32()!=Magic)throw new InvalidDataException("Invalid snapshot protocol");
   int state=reader.ReadInt32(),error=reader.ReadInt32(),length=reader.ReadInt32();long ticks=reader.ReadInt64();
   if(state<0||state>4||length<0||length>MaxBytes||state!=0&&length!=0||state==0&&length==0)throw new InvalidDataException("Invalid snapshot frame");
   byte[] bytes=null;if(length>0){bytes=new byte[length];int at=0;while(at<length){int count=reader.Read(bytes,at,length-at);if(count==0)throw new EndOfStreamException();at+=count;}}
   return new Reply{Status=state,Error=error,WriteTicks=ticks,Bytes=bytes};
  }
  public void Dispose(){AppDomain.CurrentDomain.ProcessExit-=OnProcessExit;Helper active;lock(gate){if(disposed&&helper==null)return;disposed=true;status="stopped";active=helper;helper=null;}
   if(active!=null)active.Dispose();
  }

  sealed class Helper:IDisposable {
   readonly object cleanup=new object();public Process Process;IntPtr job;bool stopped;
   public int Id;Task<string> stderr;
   public bool Exited{get{try{return Process.HasExited;}catch{return true;}}}
   public static Helper Start(string executable,string path){var value=new Helper();
    try{
     value.job=CreateJobObject(IntPtr.Zero,null);if(value.job==IntPtr.Zero)throw new IOException("Reader job unavailable");
     var limits=new ExtendedLimits();limits.Basic.LimitFlags=0x2000;
     int size=Marshal.SizeOf(typeof(ExtendedLimits));IntPtr native=Marshal.AllocHGlobal(size);
     try{Marshal.StructureToPtr(limits,native,false);if(!SetInformationJobObject(value.job,9,native,(uint)size))throw new IOException("Reader job limits unavailable");}
     finally{Marshal.FreeHGlobal(native);}
     long parentTicks;int parentId;using(var parent=Process.GetCurrentProcess()){parentTicks=parent.StartTime.ToUniversalTime().Ticks;parentId=parent.Id;}
     string encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
     var info=new ProcessStartInfo(executable,"--state-reader "+encoded+" "+parentId+" "+parentTicks){UseShellExecute=false,CreateNoWindow=true,
      WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(executable)};
     value.Process=new Process{StartInfo=info};if(!value.Process.Start())throw new IOException("Reader could not start");value.Id=value.Process.Id;
     value.stderr=value.Process.StandardError.ReadToEndAsync();
     // The child waits on stdin, so it cannot acquire a file before joining this job.
     if(!AssignProcessToJobObject(value.job,value.Process.Handle))throw new IOException("Reader could not join owned job");
     return value;
    }catch{value.Dispose();throw;}
   }
   public void Dispose(){lock(cleanup){if(stopped)return;stopped=true;
    // Closing this non-inherited job kills this helper and its descendants on parent death, too.
    if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}
    if(Process!=null){try{if(!Process.HasExited)Process.Kill();}catch{}
     try{Process.StandardInput.Dispose();}catch{}try{Process.StandardOutput.Dispose();}catch{}
     try{Process.WaitForExit(500);}catch{}try{Process.Dispose();}catch{}Process=null;
    }
   }}
   [StructLayout(LayoutKind.Sequential)] struct BasicLimits {public long ProcessTime,JobTime;public uint LimitFlags;public UIntPtr MinimumWorkingSet,MaximumWorkingSet;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
   [StructLayout(LayoutKind.Sequential)] struct IoCounters {public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
   [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits {public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemory,PeakJobMemory;}
   [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr security,string name);
   [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int kind,IntPtr information,uint length);
   [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
   [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  }
 }
}
