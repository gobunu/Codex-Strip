using System;using System.IO;using CodexStrip;
class AsyncQuestionTest {
 static int Main(){try {
 var question=J.Obj("type","agentMessage","id","call_test","text","Choose","delivery","async","questions",new[]{J.Obj("title","Choose one"),J.Obj("title","Choose two")});
 var turn=J.Obj("turnStartedAtMs",1000,"status","inProgress","items",new[]{question});
 var state=J.Obj("turns",new[]{turn});
 if(!StreamState.Project(state,"local","id").QuestionPending)throw new Exception("pending async question missing");
 foreach(string ended in new[]{"completed","interrupted","failed"}){turn["status"]=ended;if(StreamState.Project(state,"local","id").QuestionPending)throw new Exception("ended turn still waiting: "+ended);}
 turn["status"]="inProgress";
 var newer=J.Obj("turnStartedAtMs",2000,"status","inProgress","items",new[]{J.Obj("type","agentMessage","text","New work")});
 turn["status"]="completed";state["turns"]=new[]{turn,newer};if(StreamState.Project(state,"local","id").QuestionPending)throw new Exception("old question revived in new turn");state["turns"]=new[]{turn};turn["status"]="inProgress";
 var answer=J.Obj("type","steeringUserMessage","input",new[]{J.Obj("type","text","text","<send_user_message_question_reply>"+J.Json(new[]{J.Obj("questionItemId",J.Json(new object[]{"request_user_input_async","call_test",0}))})+"</send_user_message_question_reply>")});
 turn["items"]=new[]{question,answer};var partial=StreamState.Project(state,"local","id");if(!partial.QuestionPending||partial.Message!="Choose two")throw new Exception("partial answer clearing");
 answer["input"]=new[]{J.Obj("type","text","text","<send_user_message_question_reply>"+J.Json(new[]{J.Obj("questionItemId",J.Json(new object[]{"request_user_input_async","call_test",0})),J.Obj("questionItemId",J.Json(new object[]{"request_user_input_async","call_test",1}))})+"</send_user_message_question_reply>")};
 if(StreamState.Project(state,"local","id").QuestionPending)throw new Exception("answered question remains");
 if(File.Exists("work/local-stream-1.json")){var actual=J.Get(J.Get(J.Parse(File.ReadAllText("work/local-stream-1.json")),"params"),"change");if(StreamState.Project(J.Get(actual,"conversationState"),"local","id").QuestionPending)throw new Exception("real answered question remains");}
 Console.WriteLine("PASS: async question, partial answer, complete answer, recorded desktop reply");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
