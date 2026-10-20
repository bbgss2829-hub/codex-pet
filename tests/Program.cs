using CodexPet.Core;
using System.Text.Json;
using System.Text;
int passed=0,failed=0;
void Test(string name,Action test) { try {test();passed++;Console.WriteLine("PASS "+name);} catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);} }
void Eq<T>(T a,T b) { if(!EqualityComparer<T>.Default.Equals(a,b))throw new Exception($"expected {b}, actual {a}"); }
string Usage(long i,long c,long o,long t)=>JsonSerializer.Serialize(new{type="event_msg",payload=new{type="token_count",info=new{total_token_usage=new{input_tokens=i,cached_input_tokens=c,output_tokens=o,total_tokens=t}}}});
string Turn(string id)=>JsonSerializer.Serialize(new{type="event_msg",payload=new{type="task_started",turn_id=id}});
IReadOnlyList<LimitWindow> Parse(string s) { using var j=JsonDocument.Parse(s);return Limits.Parse(j.RootElement); }
Test("cache hit formula",()=>Eq(new Tokens(100,80,50,150).HitRate,80.0));
Test("zero input is missing ratio",()=>Eq(new Tokens(0,0,0,0).HitRate,null));
Test("cache miss subtracts cache only",()=>Eq(new Tokens(100,80,50,150).Miss,20L));
Test("invalid cached greater than input",()=>Eq(new Tokens(10,20,1,11).HitRate,null));
Test("large token counts",()=>Eq(new Tokens(10000000000,5000000000,1,10000000001).HitRate,50.0));
Test("cumulative snapshots not summed",()=>{var s=new SessionCounter();s.Accept(Usage(100,80,10,110));s.Accept(Usage(150,100,20,170));Eq(s.Total.Total,170L);});
Test("duplicate snapshots",()=>{var s=new SessionCounter();for(int i=0;i<5;i++)s.Accept(Usage(100,80,10,110));Eq(s.Total.Total,110L);});
Test("turn includes multiple calls",()=>{var s=new SessionCounter();s.Accept(Turn("a"));s.Accept(Usage(100,80,10,110));s.Accept(Usage(150,100,20,170));Eq(s.Answer.Total,170L);});
Test("next answer uses baseline",()=>{var s=new SessionCounter();s.Accept(Turn("a"));s.Accept(Usage(100,80,10,110));s.Accept(Turn("b"));s.Accept(Usage(150,100,20,170));Eq(s.Answer.Total,60L);Eq(s.Answer.Cached,20L);});
Test("repeated turn_context retains baseline",()=>{var s=new SessionCounter();s.Accept(Turn("a"));s.Accept(Usage(100,80,10,110));s.Accept("{\"type\":\"turn_context\",\"payload\":{\"turn_id\":\"a\"}}");s.Accept(Usage(150,100,20,170));Eq(s.Answer.Total,170L);});
Test("missing turn is explicit",()=>{var s=new SessionCounter();s.Accept(Usage(100,80,10,110));Eq(s.HasTurn,false);});
Test("null info ignored",()=>{var s=new SessionCounter();s.Accept("{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}");Eq(s.HasUsage,false);});
Test("bad lines tolerated",()=>{var s=new SessionCounter();s.Accept("{bad");s.Accept("[]");s.Accept(Usage(1,0,1,2));Eq(s.BadLines,1);Eq(s.Total.Total,2L);});
Test("counter rollback marked uncertain",()=>{var s=new SessionCounter();s.Accept(Usage(100,80,10,110));s.Accept(Usage(10,0,1,11));s.Accept(Usage(20,0,2,22));Eq(s.Uncertain,true);Eq(s.Total.Total,121L);});
Test("negative counters ignored",()=>{var s=new SessionCounter();s.Accept(Usage(-1,0,1,0));Eq(s.HasUsage,false);});
Test("turn completion changes activity",()=>{var s=new SessionCounter();s.Accept(Turn("a"));Eq(s.Active,true);s.Accept("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}");Eq(s.Active,false);});
Test("subagent metadata",()=>{var s=new SessionCounter();s.Accept("{\"type\":\"session_meta\",\"payload\":{\"id\":\"x\",\"source\":{\"subagent\":{}}}}");Eq(s.IsChild,true);});
Test("fork metadata",()=>{var s=new SessionCounter();s.Accept("{\"type\":\"session_meta\",\"payload\":{\"id\":\"x\",\"forked_from_id\":\"parent\"}}");Eq(s.IsFork,true);});
Test("actual 5h duration",()=>{var a=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300}}}");Eq(a[0].Label,"5 小时");Eq(a[0].Remaining,75.0);});
Test("weekly duration independent of slot",()=>{var a=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":12,\"windowDurationMins\":10080}}}");Eq(a[0].Label,"每周");});
Test("unknown duration not assumed",()=>Eq(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":0}}}")[0].Label,"主窗口（时长未知）"));
Test("nonweekly 10000 minutes",()=>Eq(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":10000}}}")[0].Label,"10000 分钟"));
Test("buckets preserve model limits, avoid legacy duplicate",()=>Eq(Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":5}},\"model\":{\"secondary\":{\"usedPercent\":8}}},\"rateLimits\":{\"primary\":{\"usedPercent\":5}}}").Count,2));
Test("empty map legacy fallback",()=>Eq(Parse("{\"rateLimitsByLimitId\":{},\"rateLimits\":{\"primary\":{\"usedPercent\":5}}}").Count,1));
Test("missing window not zero percent",()=>Eq(Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":{}}}").Count,0));
Test("invalid used value omitted",()=>Eq(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":\"bad\"}}}").Count,0));
Test("reset unix seconds",()=>Eq(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"resetsAt\":1700000000}}}")[0].Reset,DateTimeOffset.FromUnixTimeSeconds(1700000000)));
Test("invalid reset not crash",()=>Eq(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"resetsAt\":9223372036854775807}}}")[0].Reset,null));
Test("overlimit remaining clamped",()=>Eq(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":120}}}")[0].Remaining,0.0));
string temp=Path.Combine(Path.GetTempPath(),"CodexPetTests-"+Guid.NewGuid());Directory.CreateDirectory(temp);
try
{
Test("partial JSONL then append and repeated refresh",()=>{var p=Path.Combine(temp,"partial.jsonl");var text=Usage(100,80,10,110);File.WriteAllText(p,text[..20],new UTF8Encoding(false));var f=new SessionFile(p);f.Update();Eq(f.Counter.HasUsage,false);File.AppendAllText(p,text[20..]+"\n");f.Update();f.Update();Eq(f.Counter.Total.Total,110L);});
Test("file truncation resets state",()=>{var p=Path.Combine(temp,"truncated.jsonl");File.WriteAllText(p,Usage(100,80,10,110)+"\n"+Usage(150,100,20,170)+"\n");var f=new SessionFile(p);f.Update();File.WriteAllText(p,Usage(10,0,1,11)+"\n");f.Update();Eq(f.Counter.Total.Total,11L);});
Test("UTF8 split line",()=>{var p=Path.Combine(temp,"utf8.jsonl");var bytes=Encoding.UTF8.GetBytes("{\"type\":\"other\",\"text\":\"猫\"}\n"+Usage(1,0,1,2)+"\n");var idx=Array.IndexOf(bytes,(byte)0xE7)+1;File.WriteAllBytes(p,bytes[..idx]);var f=new SessionFile(p);f.Update();using(var stream=new FileStream(p,FileMode.Append))stream.Write(bytes[idx..]);f.Update();Eq(f.Counter.BadLines,0);Eq(f.Counter.Total.Total,2L);});
Test("duplicate session IDs deduplicate",()=>{var dir=Path.Combine(temp,"dedup");Directory.CreateDirectory(dir);var text="{\"type\":\"session_meta\",\"payload\":{\"id\":\"same\"}}\n"+Usage(1,0,1,2)+"\n";File.WriteAllText(Path.Combine(dir,"a.jsonl"),text);File.WriteAllText(Path.Combine(dir,"b.jsonl"),text);Eq(new SessionStore(dir).Scan().Count,1);});
Test("missing directory is explicit",()=>{var s=new SessionStore(Path.Combine(temp,"missing"));Eq(s.Scan().Count,0);Eq(s.Missing,true);});
}
finally { Directory.Delete(temp,true); }
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");return failed==0?0:1;
