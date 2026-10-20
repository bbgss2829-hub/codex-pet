using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexPet.Core;
namespace CodexPet;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--probe") return Probe(args[1]).GetAwaiter().GetResult();
        using var mutex=new Mutex(true,"Local\\CodexPet.MVP",out bool created);
        if(!created&&!args.Contains("--demo"))return 0;
        var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
        app.DispatcherUnhandledException+=(_,e)=> { MessageBox.Show("Codex Pet 遇到意外错误，请重新启动。\n"+e.Exception.GetType().Name,"Codex Pet"); e.Handled=true;app.Shutdown(1); };
        var w=new PetWindow(args.Contains("--demo") || args.Contains("--capture"));
        if(args.Length>=2 && args[0]=="--capture")
        {

            var output=args[1];
            w.Loaded+=async (_,_)=> { await Task.Delay(400); w.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(output))png.Save(f);w.Close(); };
        }
                if(args.Length==2 && args[0]=="--ui-check")
        {
            w.Loaded+=async (_,_)=>
            {
                await Task.Delay(10000);
                using var process=System.Diagnostics.Process.GetCurrentProcess();
                var start=process.TotalProcessorTime;var watch=System.Diagnostics.Stopwatch.StartNew();
                await Task.Delay(10000);process.Refresh();
                var visibleCpu=(process.TotalProcessorTime-start).TotalSeconds/watch.Elapsed.TotalSeconds*100;
                var visibleMemory=process.WorkingSet64/1024/1024;
                bool windowOk=w.AllowsTransparency && w.Topmost && !w.ShowInTaskbar && w.WindowStyle==WindowStyle.None;
                w.Hide();start=process.TotalProcessorTime;watch.Restart();await Task.Delay(10000);process.Refresh();
                var hiddenCpu=(process.TotalProcessorTime-start).TotalSeconds/watch.Elapsed.TotalSeconds*100;
                await File.WriteAllTextAsync(args[1],$"WindowProperties={windowOk}\nVisibleCPU_one_core_percent={visibleCpu:F2}\nHiddenCPU_one_core_percent={hiddenCpu:F2}\nWorkingSetMB={visibleMemory}\n");
                w.Close();
            };
        }
        return app.Run(w);
    }
    static async Task<int> Probe(string output)
    {
        try
        {
            await using var server=new AppServer();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var limits=await server.ReadAsync(timeout.Token);var sessions=new SessionStore().Scan();
            // Diagnostics report availability only. No token counts, account info or transcript contents.
            await File.WriteAllTextAsync(output,$"AppServer=OK\nRateLimitWindows={limits.Count}\nRootSessions={sessions.Count}\nSessionsWithUsage={sessions.Count(s=>s.Counter.HasUsage)}\n");return 0;
        }
        catch(Exception e) { await File.WriteAllTextAsync(output,"Probe=FAILED\nException="+e.GetType().Name);return 1; }
    }
}


