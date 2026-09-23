using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexStrip {
 public static class J {
  public static Dictionary<string,object> Obj(params object[] pairs) { var d=new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]]=pairs[i+1];return d; }
  public static object Get(object o,string k) { var d=o as Dictionary<string,object>;object v;return d!=null&&d.TryGetValue(k,out v)?v:null; }
  public static string Str(object o,string k,string fallback="") { return Convert.ToString(Get(o,k))??fallback; }
  public static double Num(object o,string k) { double n;return double.TryParse(Str(o,k),out n)?n:0; }
  public static object[] Arr(object o) { var a=o as object[];if(a!=null)return a;var l=o as ArrayList;return l==null?new object[0]:l.ToArray(); }
  public static string Json(object o) { return new JavaScriptSerializer{MaxJsonLength=16*1024*1024,RecursionLimit=160}.Serialize(o); }
  public static object Parse(string s) {return new JavaScriptSerializer{MaxJsonLength=16*1024*1024,RecursionLimit=160}.DeserializeObject(s);}
  public static double Now {get{return (DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;}}
 }
 public sealed class Bridge {
  public string PipeName {get;private set;}
  public string ContextId {get;private set;}
  public Bridge() {
   // App tools require a real existing task as routing context, even for reads.
   // Only its identifier is read; no auth file, remote DB, or conversation body is used.
   string inherited=Environment.GetEnvironmentVariable("CODEX_THREAD_ID");Guid valid;
   if(Guid.TryParse(inherited,out valid))ContextId=inherited;
   if(ContextId==null){try {string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);if(string.IsNullOrEmpty(home))home=Environment.GetEnvironmentVariable("USERPROFILE");string index=Path.Combine(home,".codex","session_index.jsonl");using(var stream=new FileStream(index,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){if(stream.Length>262144){stream.Seek(-262144,SeekOrigin.End);}using(var reader=new StreamReader(stream)){foreach(string line in reader.ReadToEnd().Split('\n').Reverse()){try{string id=J.Str(J.Parse(line),"id");if(Guid.TryParse(id,out valid)){ContextId=id;break;}}catch{}}}}}catch{}}
  }
  int nextId;
  public async Task Discover() {
   var candidates=new List<string>();
   // Enumerate the existing desktop pipes. No SSH, daemon or remote server is started.
   try { candidates.AddRange(Directory.GetFiles(@"\\.\pipe\").Where(p=>Path.GetFileName(p).StartsWith("codex-browser-use",StringComparison.OrdinalIgnoreCase))); } catch {}
   string supplied=Environment.GetEnvironmentVariable("CODEX_APP_TOOLS_PIPE_PATH");if(!string.IsNullOrWhiteSpace(supplied))candidates.Insert(0,supplied);
   foreach(string path in candidates.Distinct()) {
    string name=path.Replace(@"\\.\pipe\","");
    try {var result=await Rpc(name,"tools/list",J.Obj("threadStartKind","all"),2500);var tools=J.Arr(J.Get(result,"tools"));
     if(tools.Any(t=>J.Str(t,"name")=="list_threads")&&tools.Any(t=>J.Str(t,"name")=="wait_threads")){PipeName=name;return;}
    }catch{}
   }
   PipeName=null;throw new IOException("未连接 Codex，请先打开桌面应用。若仍无法连接，请以与你的 Codex 相同的用户权限启动看板。");
  }
  public void Reset(){PipeName=null;}
  public async Task<object> Call(string tool,object args,int timeout=12000) {
   if(tool!="list_threads"&&tool!="wait_threads"&&tool!="read_thread"&&tool!="navigate_to_codex_page"&&tool!="get_usage_limits")throw new InvalidOperationException("Unsupported operation");
   if(PipeName==null)await Discover();
   if(ContextId==null)throw new IOException("未找到本地任务上下文，请在 Codex 中打开一个已有本地任务后重试。");
   object result;try {result=await Rpc(PipeName,"tools/call",J.Obj("namespace","codex_app","tool",tool,"arguments",args,"threadId",ContextId,"turnId","mcp-turn-monitor-probe","callId","strip-"+Guid.NewGuid().ToString()),timeout);}catch(Exception e){throw new IOException(tool+": "+e.Message,e);}
   var content=J.Arr(J.Get(result,"contentItems"));
   if(!object.Equals(J.Get(result,"success"),true))throw new IOException(string.Join(" ",content.Select(c=>J.Str(c,"text"))));
   foreach(var c in content){string text=J.Str(c,"text");if(text.Length>0){try{return J.Parse(text);}catch{return J.Obj("text",text);}}}
   return result;
  }
  async Task<object> Rpc(string pipe,string method,object parameters,int timeout) {
   using(var s=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous)) {
    await Task.Run(()=>s.Connect(Math.Min(timeout,2000)));
    using(var cts=new CancellationTokenSource(timeout)) {
     // Dispose on timeout as named-pipe cancellation is not reliable on every Windows build.
     using(cts.Token.Register(()=>{try{s.Dispose();}catch{}})) {
      int id=Interlocked.Increment(ref nextId);byte[] p=Encoding.UTF8.GetBytes(J.Json(J.Obj("jsonrpc","2.0","id",id,"method",method,"params",parameters)));
      byte[] head=BitConverter.GetBytes(p.Length);await s.WriteAsync(head,0,4,cts.Token);await s.WriteAsync(p,0,p.Length,cts.Token);await s.FlushAsync(cts.Token);
      byte[] h=await Read(s,4,cts.Token);int n=BitConverter.ToInt32(h,0);if(n<1||n>8*1024*1024)throw new IOException("Invalid frame length");
      object response=J.Parse(Encoding.UTF8.GetString(await Read(s,n,cts.Token)));
      if(J.Get(response,"error")!=null)throw new IOException(J.Str(J.Get(response,"error"),"message"));
      return J.Get(response,"result");
     }
    }
   }
  }
  static async Task<byte[]> Read(Stream s,int length,CancellationToken token) {var b=new byte[length];int at=0;while(at<length){int n=await s.ReadAsync(b,at,length-at,token);if(n==0)throw new EndOfStreamException();at+=n;}return b;}
 }
}
