using System.Collections.Generic;

namespace CodexStrip {
 public static class UiText {
#if ENGLISH
  public static bool English {get{return true;}}
#else
  public static bool English {get{return false;}}
#endif
  public static string T(string chinese,string english){return English?english:chinese;}
  static readonly Dictionary<string,string> SystemText=new Dictionary<string,string>{
   {"正在连接 Codex…","Connecting to Codex…"},
   {"已连接 Codex","Connected to Codex"},
   {"Codex 未连接 · 等待重连","Codex disconnected · Reconnecting"},
   {"部分主机暂不可用","Some hosts are unavailable"},
   {"正在读取线程","Loading conversation"},
   {"额度暂不可读取","Usage limits unavailable"},
   {"进展","Progress"},
   {"最新回复","Latest reply"},
   {"最新进展","Latest progress"},
   {"最新对话","Latest message"},
   {"实时对话","Live conversation"}
  };
  public static string Known(string text){string translated;return English&&text!=null&&SystemText.TryGetValue(text,out translated)?translated:text;}
 }
}
