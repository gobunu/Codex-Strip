using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodexStrip {
 // Read-only follower of the desktop's existing conversation stream. No SSH or model requests.
 public sealed class DesktopStream:IDisposable {
  readonly object gate=new object();readonly SemaphoreSlim writeLock=new SemaphoreSlim(1,1);
  readonly Dictionary<string,Card> wanted=new Dictionary<string,Card>();
  readonly Dictionary<string,StreamState> states=new Dictionary<string,StreamState>();
  Dictionary<string,HashSet<string>> desktopUnread=new Dictionary<string,HashSet<string>>();
  string readIdentity="";
  public static Dictionary<string,HashSet<string>> ParseDesktopUnread(object data,string identity){
   var result=new Dictionary<string,HashSet<string>>();var saved=J.Get(data,"electron-thread-read-state-v1");if(J.Num(saved,"version")!=1)return result;
   var identities=J.Get(saved,"unreadByIdentity") as Dictionary<string,object>;if(identities==null)return result;
   object selected=null;if(identity.Length>0)identities.TryGetValue(identity,out selected);else if(identities.Count==1)selected=identities.Values.First();
   var hosts=selected as Dictionary<string,object>;if(hosts==null)return result;
   foreach(var group in hosts.GroupBy(p=>p.Key.Substring(0,Math.Max(0,p.Key.LastIndexOf(':'))))){if(group.Key.Length==0||group.Count()!=1)continue;result[group.Key]=new HashSet<string>(J.Arr(group.First().Value).Select(Convert.ToString));}return result;
  }
  public void RefreshReadState(){try{var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex",".codex-global-state.json");object data;using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){using(var r=new StreamReader(f))data=J.Parse(r.ReadToEnd());}bool changed;lock(gate){var next=ParseDesktopUnread(data,readIdentity);changed=J.Json(desktopUnread)!=J.Json(next);desktopUnread=next;}if(changed)Changed();}catch{}}
  NamedPipeClientStream pipe;bool stopped,started;string client="";public bool Connected{get;private set;}public Action Changed=delegate{};
  public void Start(){if(started)return;started=true;Task.Run((Func<Task>)Run);}
  public void DiscoverKnownThreads(){try{var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex",".codex-global-state.json");object data;using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){using(var r=new StreamReader(f))data=J.Parse(r.ReadToEnd());}var history=J.Get(J.Get(data,"electron-persisted-atom-state"),"prompt-history") as Dictionary<string,object>;if(history==null)return;var hosts=J.Arr(J.Get(data,"remote-projects")).Select(p=>J.Str(p,"hostId")).Where(h=>h.Length>0).Concat(new[]{"local"}).Distinct().ToArray();var assignments=J.Get(data,"thread-project-assignments");var candidates=new List<Card>();foreach(string id in history.Keys.OrderByDescending(x=>x,StringComparer.Ordinal).Take(64)){Guid valid;if(!Guid.TryParse(id,out valid))continue;string known=J.Str(J.Get(assignments,id),"hostId");foreach(string host in known.Length>0?new[]{known}:hosts)candidates.Add(new Card{Id=id,Host=host});}Watch(candidates);}catch{}}
  public void Watch(IEnumerable<Card> cards){var added=new List<Card>();lock(gate){foreach(var c in cards){if(!wanted.ContainsKey(c.Key)){wanted[c.Key]=c.Copy();added.Add(c.Copy());}}}if(Connected)foreach(var c in added){var t=Follow(c,true);}}
  public Card[] Latest(){lock(gate){return states.Values.Where(s=>s.Preview!=null).Select(s=>{var c=s.Preview.Copy();HashSet<string> unread;if(desktopUnread.TryGetValue(c.Host,out unread))c.Unread=unread.Contains(c.Id);c.Stale=!Connected||s.Invalid;return c;}).ToArray();}}
  async Task Send(object msg){var data=Encoding.UTF8.GetBytes(J.Json(msg));await writeLock.WaitAsync();try{var p=pipe;if(p==null||!p.IsConnected)return;await p.WriteAsync(BitConverter.GetBytes(data.Length),0,4);await p.WriteAsync(data,0,data.Length);await p.FlushAsync();}finally{writeLock.Release();}}
  async Task Follow(Card c,bool following){try{await Send(J.Obj("type","broadcast","method","thread-stream-following-changed","version",1,"sourceClientId",client,"params",J.Obj("conversationId",c.Id,"hostId",c.Host,"following",following)));}catch{try{pipe.Dispose();}catch{}}}
  async Task Run(){while(!stopped){try{using(var p=new NamedPipeClientStream(".","codex-ipc",PipeDirection.InOut,PipeOptions.Asynchronous)){pipe=p;await Task.Run(()=>p.Connect(2000));await Send(J.Obj("type","request","requestId",Guid.NewGuid().ToString(),"method","initialize","version",0,"params",J.Obj("clientType","codex-strip")));
    while(!stopped){byte[] h=await Read(p,4);int length=BitConverter.ToInt32(h,0);if(length<1||length>64*1024*1024)throw new IOException("Invalid desktop stream frame");var msg=J.Parse(Encoding.UTF8.GetString(await Read(p,length)));await Handle(msg);}
   }}catch{}finally{Connected=false;pipe=null;client="";if(!stopped)Changed();}if(!stopped)await Task.Delay(2000);}}
  async Task Handle(object msg){string type=J.Str(msg,"type"),method=J.Str(msg,"method");if(type=="client-discovery-request"){await Send(J.Obj("type","client-discovery-response","requestId",J.Str(msg,"requestId"),"response",J.Obj("canHandle",false)));return;}
   if(type=="response"&&method=="initialize"){client=J.Str(J.Get(msg,"result"),"clientId");Connected=true;Card[] all;lock(gate){all=wanted.Values.ToArray();foreach(var s in states.Values)s.Invalid=true;}foreach(var c in all)await Follow(c,true);Changed();return;}
   if(type!="broadcast")return;var p=J.Get(msg,"params");if(method=="client-status-changed"){if(J.Str(p,"status")=="connected"){Card[] all;lock(gate)all=wanted.Values.ToArray();foreach(var c in all)await Follow(c,true);}else{lock(gate){foreach(var state in states.Values)if(state.Owner==J.Str(p,"clientId"))state.Invalid=true;}Changed();}return;}
   string id=J.Str(p,"conversationId"),host=J.Str(p,"hostId");string key=host+"/"+id;
   if(method=="thread-read-state-changed"&&J.Num(msg,"version")==3){if(J.Get(p,"hasUnreadTurn") is bool){lock(gate){HashSet<string> unread;if(desktopUnread.TryGetValue(host,out unread)){if((bool)J.Get(p,"hasUnreadTurn"))unread.Add(id);else unread.Remove(id);}}Changed();}return;}
   if(method=="thread-stream-following-status-requested"){Card c;lock(gate)wanted.TryGetValue(key,out c);if(c!=null)await Follow(c,true);return;}
   if(method!="thread-stream-state-changed"||J.Num(msg,"version")!=11)return;
   bool resync=false,changed=false;Card target=null;lock(gate){if(!wanted.TryGetValue(key,out target))return;StreamState state;if(!states.TryGetValue(key,out state)){state=new StreamState();states[key]=state;}try{changed=state.Accept(J.Get(p,"change"),J.Str(msg,"sourceClientId"),host,id);resync=state.Invalid;}catch{state.Invalid=true;resync=true;}}
   if(resync){await Follow(target,false);await Follow(target,true);}else if(changed)Changed();
  }
  static async Task<byte[]> Read(Stream s,int length){var b=new byte[length];int n=0;while(n<length){int r=await s.ReadAsync(b,n,length-n);if(r==0)throw new EndOfStreamException();n+=r;}return b;}
  public void Dispose(){stopped=true;Connected=false;try{if(pipe!=null)pipe.Dispose();}catch{}}
 }

 public sealed class StreamState {
  object root;double revision;string owner="";public string Owner{get{return owner;}}public Card Preview;public bool Invalid;
  public bool Accept(object change,string source,string host,string id){string type=J.Str(change,"type");if(type=="snapshot"){root=Mutable(J.Get(change,"conversationState"));revision=J.Num(change,"revision");owner=source;Invalid=false;}
   else if(type=="patches"){if(root==null||Invalid||source!=owner||revision!=J.Num(change,"baseRevision")){Invalid=true;return false;}foreach(var patch in J.Arr(J.Get(change,"patches")))root=Patch(root,J.Arr(J.Get(patch,"path")),J.Str(patch,"op"),Mutable(J.Get(patch,"value")));revision=J.Num(change,"revision");}
   else return false;
   var next=Project(root,host,id);bool changed=Preview==null||J.Json(next)!=J.Json(Preview);Preview=next;return changed;
  }
  static object Mutable(object value){var dict=value as Dictionary<string,object>;if(dict!=null)return dict.ToDictionary(p=>p.Key,p=>Mutable(p.Value));if(value is object[]||value is ArrayList)return new ArrayList(J.Arr(value).Select(Mutable).ToArray());return value;}
  static object Patch(object root,object[] path,string op,object value){if(path.Length==0)return op=="remove"?null:value;object at=root;for(int i=0;i<path.Length-1;i++)at=at is ArrayList?((ArrayList)at)[Convert.ToInt32(path[i])]:J.Get(at,Convert.ToString(path[i]));var list=at as ArrayList;var last=path[path.Length-1];if(list!=null){int index=Convert.ToInt32(last);if(op=="remove")list.RemoveAt(index);else if(op=="add")list.Insert(index,value);else if(op=="replace")list[index]=value;else throw new IOException("Unknown stream patch");}else{var dict=(Dictionary<string,object>)at;string key=Convert.ToString(last);if(op=="remove")dict.Remove(key);else if(op=="add"||op=="replace")dict[key]=value;else throw new IOException("Unknown stream patch");}return root;}
  public static Card Project(object state,string host,string id){var card=new Card{Host=host,Id=id,Title=J.Str(state,"title"),MessageKind="实时对话",Live=true};
   var turns=new List<object>(J.Arr(J.Get(state,"turns")));var entities=J.Get(J.Get(J.Get(state,"turnHistory"),"history"),"entitiesByKey") as Dictionary<string,object>;if(entities!=null)turns.AddRange(entities.Values);
   var ordered=turns.OrderByDescending(t=>J.Num(t,"turnStartedAtMs")).ToArray();var latest=ordered.FirstOrDefault();var runtime=J.Get(state,"threadRuntimeStatus");card.Status=J.Str(runtime,"type");if(card.Status.Length==0)card.Status="unknown";
   if(latest!=null){card.Started=J.Num(latest,"turnStartedAtMs")/1000;card.Interaction=card.Started;card.TurnId=J.Str(latest,"turnId");string st=J.Str(latest,"status");if(st=="inProgress")card.Status="active";else if(st=="completed")card.Status="completed";else if(st=="failed"||st=="interrupted")card.Status=st;card.LastError=J.Str(J.Get(latest,"error"),"message");}
   card.Unread=object.Equals(J.Get(state,"hasUnreadTurn"),true);
   card.Interaction=Math.Max(card.Interaction,J.Num(state,"recencyAt")/1000);
   foreach(var t in ordered){var items=J.Arr(J.Get(t,"items"));foreach(var item in items){string kind=J.Str(item,"type");if(kind=="userMessage"||kind=="steeringUserMessage"){string messageId=J.Str(item,"id");long ms;if(messageId.Length==36&&messageId[14]=='7'&&long.TryParse(messageId.Substring(0,8)+messageId.Substring(9,4),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out ms))card.Interaction=Math.Max(card.Interaction,ms/1000.0);}}
    var agent=items.LastOrDefault(i=>J.Str(i,"type")=="agentMessage"&&!string.IsNullOrWhiteSpace(J.Str(i,"text")));if(agent!=null&&card.Message.Length==0)card.Message=J.Str(agent,"text");
   }
   var answered=new HashSet<string>();
   foreach(var t in ordered)foreach(var item in J.Arr(J.Get(t,"items"))){string kind=J.Str(item,"type");if(kind!="userMessage"&&kind!="steeringUserMessage")continue;foreach(var part in J.Arr(J.Get(item,"input")).Concat(J.Arr(J.Get(item,"content")))){string text=J.Str(part,"text");const string open="<send_user_message_question_reply>",close="</send_user_message_question_reply>";int start=text.IndexOf(open,StringComparison.Ordinal),end=text.IndexOf(close,StringComparison.Ordinal);if(start<0||end<start)continue;try{foreach(var reply in J.Arr(J.Parse(text.Substring(start+open.Length,end-start-open.Length)))){var key=J.Arr(J.Parse(J.Str(reply,"questionItemId")));if(key.Length>=3)answered.Add(Convert.ToString(key[1])+"/"+Convert.ToString(key[2]));}}catch{}}}
   // The desktop closes async question controls when their owning turn ends.
   // Historical unanswered questions must not keep the card waiting forever.
   var pending=new List<string>();foreach(var t in ordered.Where(t=>J.Str(t,"status")=="inProgress"))foreach(var item in J.Arr(J.Get(t,"items"))){if(J.Str(item,"type")!="agentMessage"||J.Str(item,"delivery")!="async")continue;var questions=J.Arr(J.Get(item,"questions"));for(int i=0;i<questions.Length;i++)if(!answered.Contains(J.Str(item,"id")+"/"+i)){string q=J.Str(questions[i],"title");if(q.Length==0)q=J.Str(questions[i],"question");if(q.Length>0)pending.Add(q);}}
   if(pending.Count>0){card.QuestionPending=true;card.Message=string.Join("\n",pending.Distinct());}
   var flags=J.Arr(J.Get(runtime,"activeFlags")).Select(Convert.ToString).ToArray();if(flags.Contains("waitingOnApproval"))card.Status="approval";if(flags.Contains("waitingOnUserInput"))card.Status="input";
   // Pending UI requests take precedence over ordinary commentary.
   foreach(var request in J.Arr(J.Get(state,"requests"))){var p=J.Get(request,"params")??request;string kind=J.Str(request,"type")+" "+J.Str(request,"method");var questions=J.Arr(J.Get(p,"questions"));if(questions.Length>0){card.QuestionPending=true;card.Message=string.Join("\n",questions.Select(q=>J.Str(q,"question").Length>0?J.Str(q,"question"):J.Str(q,"title")));}else if(kind.IndexOf("approval",StringComparison.OrdinalIgnoreCase)>=0)card.Status="approval";}
   card.MessageUnavailable=card.Message.Length==0;if(card.Message.Length>4000)card.Message=card.Message.Substring(0,4000)+"…";return card;
  }
 }
}
