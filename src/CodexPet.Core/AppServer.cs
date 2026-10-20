using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
namespace CodexPet.Core;

public sealed class AppServer : IAsyncDisposable
{
    Process? process;
    Task? pumpTask, drainTask;
    readonly ConcurrentDictionary<int,TaskCompletionSource<JsonElement>> pending=new();
    readonly SemaphoreSlim gate=new(1,1), write=new(1,1);
    int sequence;
    public event Action? LimitsChanged;
    public static string Locate()
    {
        var configured=Environment.GetEnvironmentVariable("CODEX_PET_CODEX_PATH");
        if(!string.IsNullOrWhiteSpace(configured) && File.Exists(configured) && Path.GetExtension(configured).Equals(".exe",StringComparison.OrdinalIgnoreCase)) return configured;
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        { var p=Path.Combine(dir.Trim('"'),"codex.exe"); if(File.Exists(p)) return p; }
        var bin=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(bin))
        {
            var p=Directory.EnumerateFiles(bin,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if(p is not null) return p;
        }
        throw new FileNotFoundException("未找到 codex.exe；请设置 CODEX_PET_CODEX_PATH。");
    }
    public async Task<IReadOnlyList<LimitWindow>> ReadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var token=timeout.Token;
            if(process is null || process.HasExited)
            {
                await StopAsync();
                var start=new ProcessStartInfo(Locate()) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
                foreach(var a in new[]{"app-server","--listen","stdio://","-c","analytics.enabled=false","-c","feedback.enabled=false"}) start.ArgumentList.Add(a);
                process=Process.Start(start) ?? throw new IOException("无法启动 App Server。");
                var p=process;
                pumpTask=Pump(p); drainTask=Drain(p);
                await Request("initialize",new { clientInfo=new { name="codex_pet",title="Codex Pet",version="0.1.0" } },token);
                await Send(new { method="initialized", @params=new {} },token);
            }
            return Limits.Parse(await Request("account/rateLimits/read",new {},token));
        }
        catch { await StopAsync(); throw; }
        finally { gate.Release(); }
    }
    async Task<JsonElement> Request(string method,object args,CancellationToken ct)
    {
        int id=Interlocked.Increment(ref sequence);
        var t=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id]=t;
        try { await Send(new { id,method,@params=args },ct); return await t.Task.WaitAsync(ct); }
        finally { pending.TryRemove(id,out _); }
    }
    async Task Send(object value,CancellationToken ct)
    {
        await write.WaitAsync(ct);
        try { await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(),ct); await process.StandardInput.FlushAsync(ct); }
        finally { write.Release(); }
    }
    async Task Pump(Process p)
    {
        try
        {
            while(await p.StandardOutput.ReadLineAsync() is string line)
            {
                using var d=JsonDocument.Parse(line); var r=d.RootElement;
                if(r.Num("id") is long id && pending.TryRemove((int)id,out var t))
                {
                    if(r.Get("error").ValueKind!=JsonValueKind.Undefined) t.TrySetException(new IOException("额度请求失败；请检查 Codex 自身登录及网络状态。"));
                    else t.TrySetResult(r.Get("result").Clone());
                }
                else if(r.Str("method")=="account/rateLimits/updated") LimitsChanged?.Invoke();
            }
        }
        catch(Exception e) when(e is IOException or JsonException or ObjectDisposedException or InvalidOperationException) { }
        finally { foreach(var t in pending.Values) t.TrySetException(new IOException("App Server 已断开。")); }
    }
    static async Task Drain(Process p)
    { try { while(await p.StandardError.ReadLineAsync() is not null) { } } catch(Exception e) when(e is IOException or ObjectDisposedException or InvalidOperationException) { } }
    async Task StopAsync()
    {
        var p=process; process=null; if(p is null) return;
        try { if(!p.HasExited) { p.Kill(entireProcessTree:true); await p.WaitForExitAsync(); } } catch(InvalidOperationException) { } finally { if(pumpTask is not null) await pumpTask; if(drainTask is not null) await drainTask; p.Dispose(); }
    }
    public async ValueTask DisposeAsync() { await gate.WaitAsync(); try { await StopAsync(); } finally { gate.Release(); } }
}

