using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace CodexStrip {
 // Read only user-visible assistant messages. Never surface reasoning or tool output.
 public sealed class LocalMessages {
  sealed class Tail {public string Path,Text="",Question="";public long Offset;public double Retry;}
  readonly Dictionary<string,Tail> tails=new Dictionary<string,Tail>();
  public static string AssistantText(object record){var p=J.Get(record,"payload");if(J.Str(record,"type")!="response_item"||J.Str(p,"type")!="message"||J.Str(p,"role")!="assistant")return "";string phase=J.Str(p,"phase");if(phase!="commentary"&&phase!="final_answer")return "";return string.Join("\n",J.Arr(J.Get(p,"content")).Where(c=>J.Str(c,"type")=="output_text").Select(c=>J.Str(c,"text"))).Trim();}
  public bool Waiting(Card c){lock(tails){Tail t;return tails.TryGetValue(c.Id,out t)&&t.Question.Length>0;}}
  public static string QuestionText(object record){var p=J.Get(record,"payload");string kind=J.Str(p,"type"),name=J.Str(p,"name");if(J.Str(record,"type")!="response_item"||(kind!="function_call"&&kind!="custom_tool_call")||!(name=="request_user_input_async"||name=="request_user_input"))return "";try{var args=J.Parse(J.Str(p,kind=="function_call"?"arguments":"input"));return string.Join("\n",J.Arr(J.Get(args,"questions")).Select(q=>J.Str(q,"title").Length>0?J.Str(q,"title"):J.Str(q,"question")));}catch{return "";}}
  static string Preview(Tail t){return t.Question.Length>0?t.Question:t.Text;}
  public string Read(Card card){if(card.Host!="local")return "";lock(tails){Tail t;if(!tails.TryGetValue(card.Id,out t)){t=new Tail();tails[card.Id]=t;}
   try{if(t.Path==null){if(J.Now<t.Retry)return "";t.Retry=J.Now+60;Guid id;if(!Guid.TryParse(card.Id,out id))return "";var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex","sessions");long ms;string day=null;if(card.Id[14]=='7'&&long.TryParse(card.Id.Substring(0,8)+card.Id.Substring(9,4),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out ms))day=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime().ToString("yyyy/MM/dd");string dir=day==null?root:Path.Combine(root,day);if(Directory.Exists(dir))t.Path=Directory.GetFiles(dir,"*"+card.Id+".jsonl",SearchOption.TopDirectoryOnly).FirstOrDefault();if(t.Path==null)return "";}
    using(var f=new FileStream(t.Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(f.Length<t.Offset)t.Offset=0;if(f.Length==t.Offset)return Preview(t);long start=t.Offset==0?Math.Max(0,f.Length-8*1024*1024):t.Offset;f.Seek(start,SeekOrigin.Begin);var bytes=new byte[(int)Math.Min(8*1024*1024,f.Length-start)];int n=f.Read(bytes,0,bytes.Length),end=Array.LastIndexOf(bytes,(byte)10,n-1,n);if(end<0)return Preview(t);string chunk=Encoding.UTF8.GetString(bytes,0,end+1);var lines=chunk.Split('\n');for(int i=start>0&&t.Offset==0?1:0;i<lines.Length;i++){string line=lines[i];if(!line.Contains("output_text")&&!line.Contains("request_user_input")&&!line.Contains("send_user_message_question_reply"))continue;try{var record=J.Parse(line);var payload=J.Get(record,"payload");if(J.Str(record,"type")=="response_item"&&J.Str(payload,"type")=="message"&&J.Str(payload,"role")=="user"&&line.Contains("send_user_message_question_reply"))t.Question="";string question=QuestionText(record);if(question.Length>0)t.Question=question;string msg=AssistantText(record);if(msg.Length>0)t.Text=msg.Length>4000?msg.Substring(0,4000)+"…":msg;}catch{}}t.Offset=start+end+1;}
   }catch{}return Preview(t);
  }}
 }
}

