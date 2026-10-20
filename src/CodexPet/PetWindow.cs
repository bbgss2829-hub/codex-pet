using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using CodexPet.Core;
using Forms=System.Windows.Forms;
namespace CodexPet;

public sealed class PetWindow : Window
{
    readonly Preferences settings=Preferences.Load();
    readonly AppServer server=new();
    readonly SessionStore store=new();
    readonly CancellationTokenSource stop=new();
    readonly PetView pet=new();
    readonly StackPanel root=new(), panel=new();
    readonly TextBlock summary=Text("正在连接…",13), status=Text("正在读取本地数据…",11), quota=Text("额度尚未获取",13), metrics=Text("暂无 Token 数据",13), note=Text("",11);
    readonly ComboBox tasks=new(){MinWidth=290,Margin=new Thickness(0,7,0,10)};
    readonly Forms.NotifyIcon tray;
    readonly DispatcherTimer clock=new(){Interval=TimeSpan.FromSeconds(1)}, animation=new(){Interval=TimeSpan.FromMilliseconds(250)};
    IReadOnlyList<SessionFile> sessions=[];
    IReadOnlyList<LimitWindow> limits=[];
    DateTime nextQuota=DateTime.MinValue,nextScan=DateTime.MinValue,lastQuota=DateTime.MinValue,lastInteraction=DateTime.UtcNow,happyUntil;
    bool scanning,reading,closed,updatingTasks,cleanupComplete;
    int failures;
    string? selectedId;
    Point pressed;
    bool dragged;
    readonly bool demo;
    public PetWindow(bool demo=false)
    {
        this.demo=demo;
        Title="Codex Pet · 薄荷号"; WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize; SizeToContent=SizeToContent.WidthAndHeight; Topmost=true; ShowInTaskbar=false; ShowActivated=false;
        FontFamily=new FontFamily("Microsoft YaHei UI"); Foreground=Brush("#E3EDEB");
        Left=double.IsFinite(settings.Left)?settings.Left:100; Top=double.IsFinite(settings.Top)?settings.Top:100;
        var header=new StackPanel{Orientation=Orientation.Horizontal};
        var petArea=new Border{Child=pet,Background=Brushes.Transparent,ToolTip="点击打开面板 · 拖动移动 · 滚轮缩放"};
        petArea.MouseLeftButtonDown+=(_,e)=> { pressed=e.GetPosition(this); dragged=false; petArea.CaptureMouse(); lastInteraction=DateTime.UtcNow; };
        petArea.MouseMove+=(_,e)=>
        {
            if(e.LeftButton==MouseButtonState.Pressed && petArea.IsMouseCaptured && (e.GetPosition(this)-pressed).Length>4)
            { dragged=true; petArea.ReleaseMouseCapture(); try { DragMove(); } catch(InvalidOperationException) { } KeepVisible(); Save(); }
        };
        petArea.MouseLeftButtonUp+=(_,_)=> { petArea.ReleaseMouseCapture(); if(!dragged) TogglePanel(); };
        header.Children.Add(petArea);
        var bubble=new Border{Background=Brush("#F020303A"),CornerRadius=new CornerRadius(14),Padding=new Thickness(14,10,14,10),VerticalAlignment=VerticalAlignment.Center,MaxWidth=230,Child=summary};
        header.Children.Add(bubble); root.Children.Add(header);
        var card=new Border{Background=Brush("#FA192630"),BorderBrush=Brush("#3A5359"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(20),Width=440,Child=new ScrollViewer{Content=panel,MaxHeight=580,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        card.Visibility=demo?Visibility.Visible:Visibility.Collapsed;
        panel.Tag=card; root.Children.Add(card); Content=root;
        var title=new TextBlock{Text="CODEX PET  /  薄荷号",FontSize=19,FontWeight=FontWeights.Bold,Foreground=Brush("#B5EACF")}; panel.Children.Add(title);
        panel.Children.Add(Text("本地用量伙伴  ·  独立运行",11));
        panel.Children.Add(Heading("订阅额度")); panel.Children.Add(quota);
        panel.Children.Add(Heading("Token 统计"));
        panel.Children.Add(Text("任务来源：最近活动或手动选择（不是前台选中识别）",11));
        tasks.SelectionChanged+=(_,_)=> { if(updatingTasks) return; selectedId=tasks.SelectedIndex<=0?null:(tasks.SelectedItem as TaskItem)?.Id; RenderMetrics(); };
        panel.Children.Add(tasks); panel.Children.Add(metrics); panel.Children.Add(note);
        panel.Children.Add(Text("Token 与订阅额度独立，不能直接换算。",11));
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,8)};
        AddButton(controls,"刷新",()=>{nextQuota=nextScan=DateTime.MinValue;_ = Tick();});
        AddButton(controls,"缩小",()=>Scale(-.1)); AddButton(controls,"放大",()=>Scale(.1)); AddButton(controls,"收起",TogglePanel);
        panel.Children.Add(controls); panel.Children.Add(status);
        root.LayoutTransform=new ScaleTransform(Math.Clamp(settings.Scale,.6,1.6),Math.Clamp(settings.Scale,.6,1.6));
        MouseWheel+=(_,e)=>Scale(e.Delta>0?.1:-.1);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏",null,(_,_)=>Dispatcher.Invoke(()=> { if(IsVisible) Hide(); else { Show(); KeepVisible(); } }));
        menu.Items.Add("展开监控面板",null,(_,_)=>Dispatcher.Invoke(()=> { Show(); ((Border)panel.Tag).Visibility=Visibility.Visible; KeepVisible(); }));
        menu.Items.Add("立即刷新",null,(_,_)=>Dispatcher.Invoke(()=>{ nextQuota=nextScan=DateTime.MinValue; _=Tick(); }));
        var startup=new Forms.ToolStripMenuItem("开机启动"){Checked=StartupEnabled(),CheckOnClick=false};
        startup.Click+=(_,_)=> { try { using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); if(startup.Checked) key.DeleteValue("CodexPet",false); else key.SetValue("CodexPet","\""+Environment.ProcessPath+"\""); startup.Checked=!startup.Checked; } catch(Exception e) when(e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { MessageBox.Show("无法保存开机启动设置。","Codex Pet"); } };
        menu.Items.Add(startup); menu.Items.Add("退出",null,(_,_)=>Dispatcher.Invoke(Close));
        tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Application,Text="Codex Pet · 本地用量伙伴",ContextMenuStrip=menu,Visible=!demo};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(()=> { Show(); TogglePanel(); });
        clock.Tick+=async (_,_)=>await Tick();
        animation.Tick+=(_,_)=>Animate();
        server.LimitsChanged+=()=>Dispatcher.BeginInvoke(()=>nextQuota=DateTime.MinValue);
        Loaded+=async (_,_)=> { KeepVisible(); animation.Start(); if(demo) Demo(); else { clock.Start(); await Tick(); } };
        IsVisibleChanged+=(_,_)=> { if(IsVisible) animation.Start(); else animation.Stop(); };
        Closing+=async (_,e)=> { if(cleanupComplete)return; e.Cancel=true; if(closed)return; closed=true; clock.Stop(); animation.Stop(); stop.Cancel(); tray.Visible=false; tray.Dispose(); if(!demo)Save(); await server.DisposeAsync(); cleanupComplete=true; _ = Dispatcher.BeginInvoke(Close); };
    }
    static bool StartupEnabled() { using var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return k?.GetValue("CodexPet") is string; }
    static SolidColorBrush Brush(string c) => (SolidColorBrush)new BrushConverter().ConvertFromString(c)!;
    static TextBlock Text(string s,double size) => new(){Text=s,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush(size<=11?"#A0B4BA":"#E3EDEB"),Margin=new Thickness(0,3,0,3)};
    static TextBlock Heading(string s) { var t=Text(s,14); t.FontWeight=FontWeights.SemiBold;t.Margin=new Thickness(0,16,0,6);t.Foreground=Brush("#B5EACF");return t; }
    static void AddButton(Panel p,string label,Action action) { var b=new Button{Content=label,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,7,0)}; b.Click+=(_,_)=>action(); p.Children.Add(b); }
    void Scale(double delta) { settings.Scale=Math.Clamp(settings.Scale+delta,.6,1.6);root.LayoutTransform=new ScaleTransform(settings.Scale,settings.Scale);KeepVisible();Save(); }
    void Save() { settings.Left=Left;settings.Top=Top;settings.Save(); }
    void KeepVisible()
    {
        UpdateLayout(); var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)(Left*VisualTreeHelper.GetDpi(this).DpiScaleX),(int)(Top*VisualTreeHelper.GetDpi(this).DpiScaleY)));
        var dpi=VisualTreeHelper.GetDpi(this);var a=screen.WorkingArea;
        Left=Math.Clamp(Left,a.Left/dpi.DpiScaleX,Math.Max(a.Left/dpi.DpiScaleX,a.Right/dpi.DpiScaleX-ActualWidth));
        Top=Math.Clamp(Top,a.Top/dpi.DpiScaleY,Math.Max(a.Top/dpi.DpiScaleY,a.Bottom/dpi.DpiScaleY-ActualHeight));
    }
    void TogglePanel() { var card=(Border)panel.Tag;card.Visibility=card.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible; happyUntil=DateTime.UtcNow.AddSeconds(3);KeepVisible(); }
    async Task Tick()
    {
        if(closed||demo)return;
        if(!scanning&&DateTime.UtcNow>=nextScan)
        {
            scanning=true;nextScan=DateTime.UtcNow.AddSeconds(3);
            try { sessions=await Task.Run(store.Scan); if(!closed) { UpdateTasks();RenderMetrics(); } }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException) { note.Text="会话文件暂时无法读取，稍后重试。"; }
            finally { scanning=false; }
        }
        if(!reading&&DateTime.UtcNow>=nextQuota)
        {
            reading=true;
            try
            {
                limits=await server.ReadAsync(stop.Token);lastQuota=DateTime.Now;failures=0;nextQuota=DateTime.UtcNow.AddSeconds(120);
                quota.Text=limits.Count==0?"账户未返回可用额度窗口。":string.Join("\n\n",limits.Select(l=>$"{l.Bucket} · {l.Label}   剩余 {l.Remaining:0.#}%\n已用 {l.Used:0.#}%  ·  重置 {l.Reset?.ToLocalTime().ToString("MM-dd HH:mm")??"未知"}"));
                status.Text=$"额度更新于 {lastQuota:HH:mm:ss} · 每 2 分钟刷新";happyUntil=DateTime.UtcNow.AddSeconds(2);
            }
            catch(Exception e) when(e is IOException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                failures++;var delay=Math.Min(120,5*Math.Pow(2,Math.Min(failures-1,5)));nextQuota=DateTime.UtcNow.AddSeconds(delay);
                status.Text=$"额度连接失败 · {delay:0} 秒后重试"+(lastQuota==DateTime.MinValue?"":"\n以下为上次成功数据："+lastQuota.ToString("HH:mm:ss"));
                if(lastQuota==DateTime.MinValue) quota.Text="额度不可用，请检查 Codex 登录和网络。\n未找到程序时可设置 CODEX_PET_CODEX_PATH。";
            }
            finally { reading=false;RenderSummary(); }
        }
    }
    sealed record TaskItem(string? Id,string Label) { public override string ToString()=>Label; }
    void UpdateTasks()
    {
        updatingTasks=true;
        tasks.Items.Clear();tasks.Items.Add(new TaskItem(null,"自动 · 最近活动任务"));
        foreach(var s in sessions) tasks.Items.Add(new TaskItem(s.Counter.Id,$"{s.WriteUtc.ToLocalTime():MM-dd HH:mm}  {s.Counter.Id}"));
        tasks.SelectedIndex=selectedId is null?0:Math.Max(0,tasks.Items.Cast<TaskItem>().ToList().FindIndex(x=>x.Id==selectedId));
        if(tasks.SelectedIndex==0)selectedId=null;updatingTasks=false;
    }
    SessionFile? Current => selectedId is null?sessions.FirstOrDefault():sessions.FirstOrDefault(f=>f.Counter.Id==selectedId);
    static string Counts(Tokens t) => $"输入 {t.Input:N0}   输出 {t.Output:N0}\n缓存命中 {t.Cached:N0}   未命中 {t.Miss:N0}\n总计 {t.Total:N0}   命中率 {(t.HitRate is double h?$"{h:0.0}%":"—")}";
    void RenderMetrics()
    {
        var s=Current;
        if(s is null) { metrics.Text="暂无会话数据";note.Text=store.Missing?"sessions 目录不存在或无法访问。":"等待 Codex 产生本地会话。";RenderSummary();return; }
        var c=s.Counter;
        var eligible=sessions.Where(x=>x.Counter.HasUsage&&!x.Counter.IsFork).ToArray();
        var all=eligible.Aggregate(Tokens.Zero,(a,f)=>a+f.Counter.Total);
        metrics.Text=$"当前任务\n{(c.HasUsage?Counts(c.Total):"尚无 Token 数据")}\n\n本次回答（最近 turn）\n{(c.HasTurn?Counts(c.Answer):"缺少 turn 边界，无法可靠统计")}\n\n本地累计 · {eligible.Length} 个根会话\n{Counts(all)}";
        note.Text="累计仅含当前 sessions 中可读的非派生根会话，不含归档；fork 排除以避免继承历史重复。";
        if(c.Uncertain||c.BadLines>0||s.ReadError||s.OversizedLines>0)note.Text+="\n⚠ 当前日志有计数回退、坏行或读取异常，统计可能不完整。";
        RenderSummary();
    }
    void RenderSummary()
    {
        var l=limits.Where(x=>x.Bucket=="codex").OrderBy(x=>x.Remaining).FirstOrDefault()??limits.FirstOrDefault();
        var rate=Current?.Counter.Total.HitRate;
        summary.Text=(l is null?"额度暂不可用":$"{l.Label} 剩余 {l.Remaining:0.#}%")+(failures>0?" · 旧数据":"")+"\n缓存命中 "+(rate is double h?$"{h:0.0}%":"—");
    }
    void Animate()
    {
        pet.Frame++;
        pet.Mood=DateTime.UtcNow<happyUntil?PetMood.Happy:Current?.Counter.Active==true?PetMood.Think:DateTime.UtcNow-lastInteraction>TimeSpan.FromMinutes(2)?PetMood.Rest:pet.Frame%240>220?PetMood.Walk:pet.Frame%20==0?PetMood.Blink:PetMood.Idle;
        pet.InvalidateVisual();
    }
    void Demo()
    {
        summary.Text="5 小时 剩余 76%\n缓存命中 82.4%";
        quota.Text="演示数据 · codex · 5 小时   剩余 76%\n已用 24%  ·  重置 16:30\n\n演示数据 · codex · 每周   剩余 63%\n已用 37%  ·  重置 周五 09:00";
        updatingTasks=true;tasks.Items.Add("演示任务 · 像素伙伴 MVP");tasks.SelectedIndex=0;updatingTasks=false;
        metrics.Text="当前任务\n输入 125,000   输出 8,400\n缓存命中 103,000   未命中 22,000\n总计 133,400   命中率 82.4%\n\n本次回答（最近 turn）\n输入 12,500   输出 840\n缓存命中 10,300   未命中 2,200\n总计 13,340   命中率 82.4%";
        note.Text="演示模式 · 所有数值均为虚构，不连接 Codex。";status.Text="拖动机器人移动 · 滚轮缩放 · 托盘隐藏";
    }
}



