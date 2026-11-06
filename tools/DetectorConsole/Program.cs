using NetworkMonitorTool;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// S2 收口验证：控制台驱动检测核心跑一轮（或 N 轮）周期检测并打印结果。
// 用法示例：
//   DetectorConsole --ip 127.0.0.1 --port 80 --policy Always --interval-s 2 --cycles 3
//   DetectorConsole --ip 10.255.255.1 --timeout 1500 --cycles 1   （触发失败/超时路径）

var opts = ParseArgs(args);

var services = new ServiceCollection();
services.AddLogging(b => b
    .SetMinimumLevel(LogLevel.Warning)
    .AddSimpleConsole(c => c.SingleLine = true));
services.AddDetectionCore();

await using var provider = services.BuildServiceProvider();
var scheduler = provider.GetRequiredService<ISchedulerService>();

Console.WriteLine("== NetworkMonitorTool S2 检测核心 控制台驱动 ==");
Console.WriteLine($"目标: {opts.TargetIp}"
    + (opts.HasValidPort ? $":{opts.Port}" : string.Empty)
    + $" | 策略: {opts.PortPolicy} | 超时: {opts.TimeoutMs}ms | 间隔: {opts.Interval} | 轮数: {opts.Cycles}");
Console.WriteLine(new string('-', 72));

var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
int completed = 0;

scheduler.DetectionCompleted += (_, record) =>
{
    PrintRecord(record);
    if (Interlocked.Increment(ref completed) >= opts.Cycles)
    {
        done.TrySetResult(true);
    }
};

await scheduler.StartAsync(opts.ToOptions(), opts.Interval);

// 安全上限：轮数 × 间隔 + 两次超时余量，防止目标长时间无响应时挂死
var maxWait = opts.Interval.ToTimeSpan() * opts.Cycles
    + TimeSpan.FromMilliseconds(opts.TimeoutMs * 2L)
    + TimeSpan.FromSeconds(5);

await Task.WhenAny(done.Task, Task.Delay(maxWait));

await scheduler.StopAsync();

Console.WriteLine(new string('-', 72));
Console.WriteLine($"共完成 {completed} 轮检测，调度状态: {scheduler.State}。");

return completed > 0 ? 0 : 2;

static void PrintRecord(DetectionRecord r)
{
    var ping = r.PingSuccess ? $"成功 {r.LatencyMs}ms" : "失败";
    var port = r.PortResult switch
    {
        PortProbeStatus.Success => $"成功 {r.LatencyMs}ms",
        PortProbeStatus.Failed => "失败",
        _ => "未检测",
    };
    Console.WriteLine(
        $"[{r.Timestamp:HH:mm:ss.fff}] {r.TargetIp}({r.Port?.ToString() ?? "-"}) " +
        $"Ping={ping} | 端口={port} | 状态={r.Status}" +
        (r.ErrorSummary is null ? string.Empty : $" | 原因: {r.ErrorSummary}"));
}

static RunOptions ParseArgs(string[] args)
{
    var o = new RunOptions();
    for (int i = 0; i < args.Length; i++)
    {
        string? Next() => i + 1 < args.Length ? args[++i] : null;
        switch (args[i].ToLowerInvariant())
        {
            case "--ip": o.TargetIp = Next() ?? o.TargetIp; break;
            case "--port": if (int.TryParse(Next(), out var p)) o.Port = p; break;
            case "--timeout": if (int.TryParse(Next(), out var t)) o.TimeoutMs = t; break;
            case "--cycles": if (int.TryParse(Next(), out var c) && c > 0) o.Cycles = c; break;
            case "--interval-h": if (int.TryParse(Next(), out var h)) o.Interval = o.Interval with { Hours = h }; break;
            case "--interval-m": if (int.TryParse(Next(), out var m)) o.Interval = o.Interval with { Minutes = m }; break;
            case "--interval-s": if (int.TryParse(Next(), out var s)) o.Interval = o.Interval with { Seconds = s }; break;
            case "--policy":
                if (Enum.TryParse<PortDetectionPolicy>(Next(), ignoreCase: true, out var pol)) o.PortPolicy = pol;
                break;
        }
    }

    if (!o.Interval.IsValid)
    {
        o.Interval = new DetectionInterval(0, 0, 2);
    }

    return o;
}

internal sealed class RunOptions
{
    public string TargetIp { get; set; } = "127.0.0.1";
    public int? Port { get; set; }
    public int TimeoutMs { get; set; } = 3000;
    public PortDetectionPolicy PortPolicy { get; set; } = PortDetectionPolicy.OnlyWhenPingSuccess;
    public DetectionInterval Interval { get; set; } = new(0, 0, 2);
    public int Cycles { get; set; } = 1;

    public bool HasValidPort => Port is >= 1 and <= 65535;

    public DetectionOptions ToOptions() => new()
    {
        TargetIp = TargetIp,
        Port = Port,
        TimeoutMs = TimeoutMs,
        PortPolicy = PortPolicy,
    };
}
