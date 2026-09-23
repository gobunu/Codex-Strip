using System;using System.IO;using System.Linq;using CodexStrip;
class DesktopReadTest {static int Main(){try{
 var host=J.Obj("local:machine",new[]{"unread"},"remote-ssh-discovered:virgo:machine",new string[0]);
 var data=J.Obj("electron-thread-read-state-v1",J.Obj("version",1,"unreadByIdentity",J.Obj("account",host)));
 var map=DesktopStream.ParseDesktopUnread(data,"");if(!map["local"].Contains("unread")||map["local"].Contains("read")||map["remote-ssh-discovered:virgo"].Count!=0)throw new Exception("canonical map");
 host["local:machine"]=new string[0];if(DesktopStream.ParseDesktopUnread(data,"")["local"].Contains("unread"))throw new Exception("view without sending did not clear");
 var identities=J.Get(J.Get(data,"electron-thread-read-state-v1"),"unreadByIdentity") as System.Collections.Generic.Dictionary<string,object>;identities["other"]=host;if(DesktopStream.ParseDesktopUnread(data,"").Count!=0)throw new Exception("ambiguous accounts merged");
 var actual=J.Parse(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex",".codex-global-state.json")));var live=DesktopStream.ParseDesktopUnread(actual,"");Console.WriteLine("PASS: canonical unread store, read clearing, account isolation; live hosts="+live.Count+", unread="+live.Values.Sum(v=>v.Count));return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}}
