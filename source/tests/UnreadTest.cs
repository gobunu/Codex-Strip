using System;using CodexStrip;
class UnreadTest {
 static int Main(){try{
 var turn=J.Obj("status","completed","turnStartedAtMs",1000,"items",new[]{J.Obj("type","agentMessage","text","Done")});
 var state=J.Obj("hasUnreadTurn",true,"turns",new[]{turn});var stream=new StreamState();
 stream.Accept(J.Obj("type","snapshot","revision",1,"conversationState",state),"owner","remote","id");
 if(!stream.Preview.UnreadResult)throw new Exception("unread completed missing");
 bool changed=stream.Accept(J.Obj("type","patches","baseRevision",1,"revision",2,"patches",new[]{J.Obj("op","replace","path",new object[]{"hasUnreadTurn"},"value",false)}),"owner","remote","id");
 if(!changed||stream.Preview.UnreadResult)throw new Exception("read event did not clear");
 turn["status"]="inProgress";if(StreamState.Project(state,"remote","id").UnreadResult)throw new Exception("active turn highlighted as completed");
 turn["status"]="failed";if(StreamState.Project(state,"remote","id").UnreadResult)throw new Exception("failed turn highlighted as completed");
 state["hasUnreadTurn"]=false;turn["status"]="completed";if(StreamState.Project(state,"remote","id").UnreadResult)throw new Exception("read history highlighted");
 Console.WriteLine("PASS: unread completion, read delta notification, active/failed/read exclusions");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
