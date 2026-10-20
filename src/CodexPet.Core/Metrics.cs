using System.Text.Json;
namespace CodexPet.Core;

public static class J
{
    public static JsonElement Get(this JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;
    public static string? Str(this JsonElement e, string key) => e.Get(key).ValueKind == JsonValueKind.String ? e.Get(key).GetString() : null;
    public static long? Num(this JsonElement e, string key) => e.Get(key).ValueKind == JsonValueKind.Number && e.Get(key).TryGetInt64(out var n) ? n : null;
}
public record Tokens(long Input, long Cached, long Output, long Total)
{
    public static readonly Tokens Zero = new(0,0,0,0);
    public long Miss => Math.Max(0, Input - Cached);
    public double? HitRate => Input > 0 && Cached <= Input ? Cached * 100.0 / Input : null;
    public static Tokens operator +(Tokens a, Tokens b) => new(a.Input+b.Input,a.Cached+b.Cached,a.Output+b.Output,a.Total+b.Total);
    public static Tokens operator -(Tokens a, Tokens b) => new(a.Input-b.Input,a.Cached-b.Cached,a.Output-b.Output,a.Total-b.Total);
    public bool NonNegative => Input >= 0 && Cached >= 0 && Output >= 0 && Total >= 0;
    public static Tokens? Parse(JsonElement e) => e.Num("input_tokens") is long i && e.Num("cached_input_tokens") is long c && e.Num("output_tokens") is long o && e.Num("total_tokens") is long t && i>=0 && c>=0 && o>=0 && t>=0 ? new(i,c,o,t) : null;
}
public record LimitWindow(string Bucket, string Slot, long? Minutes, double Used, DateTimeOffset? Reset)
{
    public double Remaining => Math.Clamp(100-Used,0,100);
    public string Label => Minutes switch { 10080 => "每周", >0 when Minutes%1440==0 => $"{Minutes/1440} 天", >0 when Minutes%60==0 => $"{Minutes/60} 小时", >0 => $"{Minutes} 分钟", _ => Slot=="primary" ? "主窗口（时长未知）" : "次窗口（时长未知）" };
}
public static class Limits
{
    public static IReadOnlyList<LimitWindow> Parse(JsonElement root)
    {
        List<LimitWindow> result=[];
        var map=root.Get("rateLimitsByLimitId");
        if(map.ValueKind==JsonValueKind.Object) foreach(var p in map.EnumerateObject()) Add(p.Value,p.Name,result);
        // Legacy bucket is an alias. Never count it twice; allow empty maps to fall back.
        if(result.Count==0) Add(root.Get("rateLimits"),"codex",result);
        return result;
    }
    static void Add(JsonElement bucket,string id,List<LimitWindow> list)
    {
        foreach(var slot in new[]{"primary","secondary"})
        {
            var w=bucket.Get(slot); var used=w.Get("usedPercent");
            if(used.ValueKind!=JsonValueKind.Number || !used.TryGetDouble(out var n) || !double.IsFinite(n)) continue;
            DateTimeOffset? reset=null;
            if(w.Num("resetsAt") is long ts) try { reset=DateTimeOffset.FromUnixTimeSeconds(ts); } catch(ArgumentOutOfRangeException) { }
            list.Add(new(bucket.Str("limitId")??id,slot,w.Num("windowDurationMins"),n,reset));
        }
    }
}
// Holds counters only; neither text nor raw events are retained.
public sealed class SessionCounter
{
    public string Id {get;private set;}="";
    public bool IsChild {get;private set;}
    public bool IsFork {get;private set;}
    public bool Active {get;private set;}
    public bool HasUsage {get;private set;}
    public bool Uncertain {get;private set;}
    public int BadLines {get;private set;}
    public string? TurnId {get;private set;}
    public Tokens Total {get;private set;}=Tokens.Zero;
    public Tokens Answer {get;private set;}=Tokens.Zero;
    public bool HasTurn {get;private set;}
    Tokens? previous;
    Tokens baseline=Tokens.Zero;
    public void Accept(string line)
    {
        try
        {
            using var doc=JsonDocument.Parse(line); var r=doc.RootElement; var p=r.Get("payload");
            switch(r.Str("type"))
            {
                case "session_meta":
                    Id=p.Str("id")??Id; IsChild=p.Get("source").Get("subagent").ValueKind!=JsonValueKind.Undefined;
                    IsFork=p.Str("forked_from_id") is not null; return;
                case "turn_context":
                    Begin(p.Str("turn_id")); return;
                case "event_msg": break;
                default: return;
            }
            switch(p.Str("type"))
            {
                case "task_started": Begin(p.Str("turn_id")); Active=true; return;
                case "task_complete": case "task_completed": case "turn_aborted": Active=false; return;
                case "token_count": break;
                default: return;
            }
            var t=Tokens.Parse(p.Get("info").Get("total_token_usage"));
            if(t is null) return;
            if(previous is not null && t==previous) return; // repeated snapshots and rate-only events
            if(previous is null) Total=t;
            else
            {
                var delta=t-previous;
                if(delta.NonNegative) Total+=delta;
                else { Uncertain=true; } // reset or out-of-order snapshot: rebase, do not invent usage
            }
            previous=t; HasUsage=true;
            if(HasTurn) Answer=Total-baseline;
        }
        catch(JsonException) { BadLines++; }
    }
    void Begin(string? id)
    {
        if(string.IsNullOrEmpty(id) || id==TurnId) return;
        TurnId=id; HasTurn=true; baseline=Total; Answer=Tokens.Zero;
    }
}
