using System;
using System.Linq;
using CodexStrip;
class PipeDiscoveryTest {
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static int Main(){try{
  var modern=Bridge.DiscoveryCandidates(new[]{@"\\.\pipe\codex-app-tools-current"});
  Check(modern.SequenceEqual(new[]{"codex-app-tools-current"}),"new app-tools pipe was not discovered");
  var mixed=Bridge.DiscoveryCandidates(new[]{@"\\.\pipe\codex-browser-use-old",@"\\.\pipe\codex-app-tools-current",@"\\.\pipe\unrelated",@"\\.\pipe\CODEX-APP-TOOLS-CURRENT"});
  Check(mixed.SequenceEqual(new[]{"codex-app-tools-current","codex-browser-use-old"}),"current pipe was not preferred or duplicate/unrelated pipe leaked");
  var nested=Bridge.DiscoveryCandidates(new[]{@"\\.\pipe\codex-app-tools\current",@"\\.\pipe\codex-browser-use\old"});
  Check(nested.SequenceEqual(new[]{@"codex-app-tools\current",@"codex-browser-use\old"}),"namespaced pipe lost its prefix");
  var supplied=Bridge.DiscoveryCandidates(mixed,@"\\.\pipe\explicit-routing-pipe");
  Check(supplied[0]=="explicit-routing-pipe"&&supplied.Length==3,"inherited pipe did not retain priority");
  Check(Bridge.DiscoveryCandidates(mixed,@"\\.\pipe\codex-app-tools-current").Length==2,"inherited pipe was probed twice");
  Check(Bridge.DiscoveryCandidates(new[]{"",null,"  unrelated  "}," ").Length==0,"blank or unrelated pipes were accepted");
  Check(Bridge.DiscoveryCandidates(null).Length==0,"missing inventory failed");
  Check(Bridge.DiscoveryCandidates(new[]{"codex-browser-use-legacy"}).Single()=="codex-browser-use-legacy","legacy desktop compatibility was lost");
  Console.WriteLine("PASS: "+checks+" app-tools discovery, priority, namespaced, inherited and legacy cases");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
