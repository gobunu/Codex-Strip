using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CodexStrip {
 public sealed class ResourceSnapshot {
  public DateTime Time=DateTime.Now;
  public double? Cpu,Gpu,Memory,Download,Upload,DiskRead,DiskWrite;
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
  Dictionary<string,CounterSample> gpuPrevious=new Dictionary<string,CounterSample>();double? lastGpu;DateTime lastGpuTime;bool gpuUnavailable;double gpuDivisor=1;

  public static NetworkInterface[] Adapters(){try{return NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType!=NetworkInterfaceType.Loopback&&n.NetworkInterfaceType!=NetworkInterfaceType.Tunnel).OrderBy(n=>n.Name).ToArray();}catch{return new NetworkInterface[0];}}
  static bool Gateway(NetworkInterface n){try{return n.GetIPProperties().GatewayAddresses.Count>0;}catch{return false;}}
  NetworkInterface SelectAdapter(string id){var all=Adapters().Where(n=>n.OperationalStatus==OperationalStatus.Up).ToArray();if(!string.IsNullOrEmpty(id))return all.FirstOrDefault(n=>n.Id==id);return all.OrderByDescending(Gateway).ThenByDescending(n=>n.NetworkInterfaceType==NetworkInterfaceType.Ethernet||n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211).ThenByDescending(n=>n.Speed).FirstOrDefault();}

  public ResourceSnapshot Sample(string adapterId){var result=new ResourceSnapshot();
   FileTime idle,kernel,user;if(GetSystemTimes(out idle,out kernel,out user)){ulong total=kernel.Value+user.Value;if(hasCpu&&total>lastTotal){ulong busy=(total-lastTotal)-Math.Min(idle.Value-lastIdle,total-lastTotal);result.Cpu=Math.Max(0,Math.Min(100,busy*100.0/(total-lastTotal)));}lastIdle=idle.Value;lastTotal=total;hasCpu=true;}
   var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf(typeof(MemoryStatus))};if(GlobalMemoryStatusEx(ref memory)&&memory.TotalPhysical>0)result.Memory=Math.Max(0,Math.Min(100,(memory.TotalPhysical-memory.AvailablePhysical)*100.0/memory.TotalPhysical));
   var adapter=SelectAdapter(adapterId);if(adapter!=null){try{var stats=adapter.GetIPv4Statistics();result.NetworkName=adapter.Name;var now=DateTime.UtcNow;double seconds=(now-lastNetworkTime).TotalSeconds;if(adapter.Id==lastAdapter&&seconds>.2&&seconds<60){result.Download=Math.Max(0,(stats.BytesReceived-lastReceived)/seconds);result.Upload=Math.Max(0,(stats.BytesSent-lastSent)/seconds);}lastAdapter=adapter.Id;lastReceived=stats.BytesReceived;lastSent=stats.BytesSent;lastNetworkTime=now;}catch{}}
   if(!diskUnavailable)try{if(diskRead==null){diskRead=new PerformanceCounter("PhysicalDisk","Disk Read Bytes/sec","_Total",true);diskWrite=new PerformanceCounter("PhysicalDisk","Disk Write Bytes/sec","_Total",true);diskRead.NextValue();diskWrite.NextValue();}else{result.DiskRead=Math.Max(0,diskRead.NextValue());result.DiskWrite=Math.Max(0,diskWrite.NextValue());}}catch{diskUnavailable=true;}
   result.Gpu=ReadGpu();return result;
  }
  double? ReadGpu(){if(gpuUnavailable)return null;if((DateTime.UtcNow-lastGpuTime).TotalSeconds<2)return lastGpu;try{
    var category=new PerformanceCounterCategory("GPU Engine");var all=category.ReadCategory();var values=all["Utilization Percentage"];var next=new Dictionary<string,CounterSample>();var engines=new Dictionary<string,double>();double largestInstance=0;
    foreach(DictionaryEntry item in values){string instance=Convert.ToString(item.Key);var data=item.Value as InstanceData;if(data==null)continue;next[instance]=data.Sample;CounterSample prior;if(!gpuPrevious.TryGetValue(instance,out prior))continue;int marker=instance.IndexOf("_luid_",StringComparison.OrdinalIgnoreCase);if(marker<0)continue;double value;try{value=CounterSample.Calculate(prior,data.Sample);}catch{continue;}if(double.IsNaN(value)||double.IsInfinity(value)||value<0)continue;largestInstance=Math.Max(largestInstance,value);string engine=instance.Substring(marker+1);double sum;engines.TryGetValue(engine,out sum);engines[engine]=sum+value;}
    // Some WDDM drivers report a single process above 100%; on those systems
    // GPU Engine percentages are scaled by 100. Keep that calibration for this run.
    if(largestInstance>100)gpuDivisor=100;
    gpuPrevious=next;lastGpu=engines.Count>0?(double?)Math.Max(0,Math.Min(100,engines.Values.Max()/gpuDivisor)):null;lastGpuTime=DateTime.UtcNow;return lastGpu;
   }catch{gpuUnavailable=true;return null;}}
  public void Dispose(){if(diskRead!=null)diskRead.Dispose();if(diskWrite!=null)diskWrite.Dispose();}
 }
}
