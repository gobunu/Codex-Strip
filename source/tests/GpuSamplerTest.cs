using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CodexStrip;
class GpuSamplerTest {
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static readonly MethodInfo Update=typeof(ResourceSampler).GetMethod("UpdateGpu",Private);
 static CounterSample Sample(long raw,long stamp){return new CounterSample(raw,0,10000000,10000000,stamp,stamp,PerformanceCounterType.Timer100Ns);}
 static string Instance(int pid,string device,int engine){return "pid_"+pid+"_luid_"+device+"_phys_0_eng_"+engine+"_engtype_3d";}
 static double? Feed(ResourceSampler sampler,Dictionary<string,CounterSample> values){Update.Invoke(sampler,new object[]{values});return (double?)typeof(ResourceSampler).GetField("lastGpu",Private).GetValue(sampler);}
 static void Near(double? actual,double expected,string message){if(!actual.HasValue||Math.Abs(actual.Value-expected)>.001)throw new Exception(message+": "+actual);}
 static ResourcePart[] Parts(ResourceSampler sampler){return (ResourcePart[])typeof(ResourceSampler).GetField("lastGpuParts",Private).GetValue(sampler);}
 static int Main(){try{int pid=Process.GetCurrentProcess().Id;string game=Instance(pid,"0x0_0x1",0),other=Instance(0,"0x0_0x1",1),integrated=Instance(0,"0x0_0x2",0);
  using(var sampler=new ResourceSampler()){
   var values=new Dictionary<string,CounterSample>{{game,Sample(0,10000000)},{other,Sample(0,10000000)},{integrated,Sample(0,10000000)}};
   if(Feed(sampler,values).HasValue)throw new Exception("first sample did not warm up");
   values=new Dictionary<string,CounterSample>{{game,Sample(7900000,20000000)},{other,Sample(600000,20000000)},{integrated,Sample(0,20000000)}};Near(Feed(sampler,values),79,"busy discrete GPU was diluted by idle engines");Near(Parts(sampler).Sum(p=>p.Percent),79,"process share was diluted");
   values=new Dictionary<string,CounterSample>{{game,Sample(15800000,30000000)},{other,Sample(20600000,30000000)},{integrated,Sample(0,30000000)}};Near(Feed(sampler,values),100,"over-range sample was not bounded");
   values=new Dictionary<string,CounterSample>{{game,Sample(23700000,40000000)},{other,Sample(20900000,40000000)},{integrated,Sample(0,40000000)}};Near(Feed(sampler,values),79,"200% transient permanently scaled later readings");Near(Parts(sampler).Sum(p=>p.Percent),79,"transient scaled later process share");
   values=new Dictionary<string,CounterSample>{{game,Sample(31600000,50000000)},{other,Sample(2020900000,50000000)}};Near(Feed(sampler,values),100,"large transient not bounded");
   values=new Dictionary<string,CounterSample>{{game,Sample(39500000,60000000)},{other,Sample(2021200000,60000000)}};Near(Feed(sampler,values),79,"large transient permanently scaled later readings");
   Feed(sampler,new Dictionary<string,CounterSample>{{game,Sample(10,70000000)}});
   Near(Feed(sampler,new Dictionary<string,CounterSample>{{game,Sample(7900010,80000000)}}),79,"reset counter did not recover from its new baseline");
   if(Feed(sampler,new Dictionary<string,CounterSample>{{game,Sample(8900010,80000000)}}).HasValue)throw new Exception("zero-duration sample was accepted");
   Near(Feed(sampler,new Dictionary<string,CounterSample>{{game,Sample(16800010,90000000)}}),79,"zero-duration baseline did not recover");
  }
  using(var sampler=new ResourceSampler()){
   string second=Instance(0,"0x0_0x1",0);Feed(sampler,new Dictionary<string,CounterSample>{{game,Sample(0,10000000)},{second,Sample(0,10000000)}});
   Near(Feed(sampler,new Dictionary<string,CounterSample>{{game,Sample(6000000,20000000)},{second,Sample(1900000,20000000)}}),79,"processes on the same physical engine were not summed");Near(Parts(sampler).Sum(p=>p.Percent),79,"process shares did not match engine total");
  }
  Console.WriteLine("PASS: GPU engine aggregation; idle second GPU; process shares; 200% and 20000% transient recovery; counter reset; zero-duration recovery");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
