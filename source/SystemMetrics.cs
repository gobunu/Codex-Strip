using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CodexStrip {
 public sealed class ResourcePart {
  public string Name;public double Percent;
  public ResourcePart(string name,double percent){Name=name;Percent=percent;}
 }
 public sealed class ResourceSnapshot {
  public DateTime Time=DateTime.Now;
  public double? Cpu,Gpu,Memory,Download,Upload,DiskRead,DiskWrite;
  public ResourcePart[] CpuProcesses=new ResourcePart[0],GpuProcesses=new ResourcePart[0],MemoryProcesses=new ResourcePart[0];
  public string NetworkName="";
 }

 public sealed class ResourceSampler:IDisposable {
  [StructLayout(LayoutKind.Sequential)]struct FileTime {public uint Low,High;public ulong Value {get{return ((ulong)High<<32)|Low;}}}
  [StructLayout(LayoutKind.Sequential)]struct MemoryStatus {
   public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
  }
  [DllImport("kernel32.dll")]static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
  [DllImport("kernel32.dll")]static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
  ulong lastIdle,lastTotal;bool hasCpu;
  string lastAdapter="";long lastReceived,lastSent;DateTime lastNetworkTime;
  PerformanceCounter diskRead,diskWrite;bool diskUnavailable;
  Dictionary<int,long> processPrevious=new Dictionary<int,long>();DateTime lastProcessTime;
  Dictionary<string,CounterSample> gpuPrevious=new Dictionary<string,CounterSample>();double? lastGpu;ResourcePart[] lastGpuParts=new ResourcePart[0];DateTime lastGpuTime;bool gpuUnavailable;double gpuDivisor=1;

  public static NetworkInterface[] Adapters(){try{return NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType!=NetworkInterfaceType.Loopback&&n.NetworkInterfaceType!=NetworkInterfaceType.Tunnel).OrderBy(n=>n.Name).ToArray();}catch{return new NetworkInterface[0];}}
  static bool Gateway(NetworkInterface n){try{return n.GetIPProperties().GatewayAddresses.Count>0;}catch{return false;}}
  NetworkInterface SelectAdapter(string id){var all=Adapters().Where(n=>n.OperationalStatus==OperationalStatus.Up).ToArray();if(!string.IsNullOrEmpty(id))return all.FirstOrDefault(n=>n.Id==id);return all.OrderByDescending(Gateway).ThenByDescending(n=>n.NetworkInterfaceType==NetworkInterfaceType.Ethernet||n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211).ThenByDescending(n=>n.Speed).FirstOrDefault();}

  public ResourceSnapshot Sample(string adapterId){var result=new ResourceSnapshot();
   FileTime idle,kernel,user;if(GetSystemTimes(out idle,out kernel,out user)){ulong total=kernel.Value+user.Value;if(hasCpu&&total>lastTotal){ulong busy=(total-lastTotal)-Math.Min(idle.Value-lastIdle,total-lastTotal);result.Cpu=Math.Max(0,Math.Min(100,busy*100.0/(total-lastTotal)));}lastIdle=idle.Value;lastTotal=total;hasCpu=true;}
   var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf(typeof(MemoryStatus))};if(GlobalMemoryStatusEx(ref memory)&&memory.TotalPhysical>0)result.Memory=Math.Max(0,Math.Min(100,(memory.TotalPhysical-memory.AvailablePhysical)*100.0/memory.TotalPhysical));
   ReadProcesses(result,memory.TotalPhysical);
   var adapter=SelectAdapter(adapterId);if(adapter!=null){try{var stats=adapter.GetIPv4Statistics();result.NetworkName=adapter.Name;var now=DateTime.UtcNow;double seconds=(now-lastNetworkTime).TotalSeconds;if(adapter.Id==lastAdapter&&seconds>.2&&seconds<60){result.Download=Math.Max(0,(stats.BytesReceived-lastReceived)/seconds);result.Upload=Math.Max(0,(stats.BytesSent-lastSent)/seconds);}lastAdapter=adapter.Id;lastReceived=stats.BytesReceived;lastSent=stats.BytesSent;lastNetworkTime=now;}catch{}}
   if(!diskUnavailable)try{if(diskRead==null){diskRead=new PerformanceCounter("PhysicalDisk","Disk Read Bytes/sec","_Total",true);diskWrite=new PerformanceCounter("PhysicalDisk","Disk Write Bytes/sec","_Total",true);diskRead.NextValue();diskWrite.NextValue();}else{result.DiskRead=Math.Max(0,diskRead.NextValue());result.DiskWrite=Math.Max(0,diskWrite.NextValue());}}catch{diskUnavailable=true;}
   result.Gpu=ReadGpu(result);return result;
  }
  static ResourcePart[] TopParts(Dictionary<string,double> values,double? total){if(!total.HasValue||total.Value<=0)return new ResourcePart[0];var top=values.Where(p=>p.Value>0.05).OrderByDescending(p=>p.Value).Take(4).ToArray();double sum=top.Sum(p=>p.Value),scale=sum>total.Value?total.Value/sum:1;return top.Select(p=>new ResourcePart(p.Key,Math.Max(0,p.Value*scale))).ToArray();}
  void ReadProcesses(ResourceSnapshot result,ulong totalMemory){
   var next=new Dictionary<int,long>();var cpu=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);var mem=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
   DateTime now=DateTime.UtcNow;double seconds=(now-lastProcessTime).TotalSeconds;
   foreach(var process in Process.GetProcesses()){try{
    int id=process.Id;string name=process.ProcessName;long ticks=process.TotalProcessorTime.Ticks;next[id]=ticks;
    long prior;if(seconds>.2&&seconds<60&&processPrevious.TryGetValue(id,out prior)&&ticks>=prior){double share=(ticks-prior)*100.0/(TimeSpan.TicksPerSecond*seconds*Math.Max(1,Environment.ProcessorCount));double value;cpu.TryGetValue(name,out value);cpu[name]=value+Math.Max(0,share);}
    if(totalMemory>0){double share=process.WorkingSet64*100.0/totalMemory;double value;mem.TryGetValue(name,out value);mem[name]=value+Math.Max(0,share);}
   }catch{}finally{process.Dispose();}}
   processPrevious=next;lastProcessTime=now;
   result.CpuProcesses=TopParts(cpu,result.Cpu);result.MemoryProcesses=TopParts(mem,result.Memory);
  }
  static string ProcessName(int pid){try{using(var process=Process.GetProcessById(pid))return process.ProcessName;}catch{return "PID "+pid;}}
  double? ReadGpu(ResourceSnapshot result){result.GpuProcesses=lastGpuParts;if(gpuUnavailable)return null;if((DateTime.UtcNow-lastGpuTime).TotalSeconds<2)return lastGpu;try{
    var category=new PerformanceCounterCategory("GPU Engine");var all=category.ReadCategory();var values=all["Utilization Percentage"];var next=new Dictionary<string,CounterSample>();var engines=new Dictionary<string,double>();var engineProcesses=new Dictionary<string,Dictionary<int,double>>();double largestInstance=0;
    foreach(DictionaryEntry item in values){string instance=Convert.ToString(item.Key);var data=item.Value as InstanceData;if(data==null)continue;next[instance]=data.Sample;CounterSample prior;if(!gpuPrevious.TryGetValue(instance,out prior))continue;int marker=instance.IndexOf("_luid_",StringComparison.OrdinalIgnoreCase);if(marker<0)continue;double value;try{value=CounterSample.Calculate(prior,data.Sample);}catch{continue;}if(double.IsNaN(value)||double.IsInfinity(value)||value<0)continue;largestInstance=Math.Max(largestInstance,value);string engine=instance.Substring(marker+1);double sum;engines.TryGetValue(engine,out sum);engines[engine]=sum+value;int start=instance.IndexOf("pid_",StringComparison.OrdinalIgnoreCase);if(start>=0){start+=4;int end=instance.IndexOf('_',start);int pid;if(end>start&&int.TryParse(instance.Substring(start,end-start),out pid)){Dictionary<int,double> processes;if(!engineProcesses.TryGetValue(engine,out processes)){processes=new Dictionary<int,double>();engineProcesses[engine]=processes;}double current;processes.TryGetValue(pid,out current);processes[pid]=current+value;}}}
    // Some WDDM drivers return GPU Engine values scaled by 100 or 1000.
    // Keep the detected scale for subsequent samples in this run.
    if(largestInstance>1000)gpuDivisor=1000;else if(largestInstance>100&&gpuDivisor<100)gpuDivisor=100;
    gpuPrevious=next;string busiest=engines.OrderByDescending(p=>p.Value).Select(p=>p.Key).FirstOrDefault();lastGpu=busiest!=null?(double?)Math.Max(0,Math.Min(100,engines[busiest]/gpuDivisor)):null;lastGpuParts=new ResourcePart[0];Dictionary<int,double> byPid;if(busiest!=null&&engineProcesses.TryGetValue(busiest,out byPid)){var names=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);foreach(var p in byPid.OrderByDescending(p=>p.Value).Take(12)){string name=ProcessName(p.Key);double old;names.TryGetValue(name,out old);names[name]=old+p.Value/gpuDivisor;}lastGpuParts=TopParts(names,lastGpu);}result.GpuProcesses=lastGpuParts;lastGpuTime=DateTime.UtcNow;return lastGpu;
   }catch{gpuUnavailable=true;return null;}}
  public void Dispose(){if(diskRead!=null)diskRead.Dispose();if(diskWrite!=null)diskWrite.Dispose();}
 }
}
