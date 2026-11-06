using System.IO;
using NetworkMonitorTool;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// S2/S3 收口验证：控制台驱动检测核心跑一轮（或 N 轮）周期检测、写入 SQLite 并回读历史。
// 用法示例：
//   DetectorConsole --ip 127.0.0.1 --port 80 --policy Always --interval-s 2 --cycles 3
//   DetectorConsole --ip 10.255.255.1 --timeout 1500 --cycles 1        （触发失败/超时路径）
//   DetectorConsole --cycles 2 --data-dir D:\Temp\NmtData              （指定数据目录，不写配置）
//   DetectorConsole --save-config --ip 192.168.1.1 --port 80           （校验并写入 appsettings.json）
//   DetectorConsole --migrate D:\Temp\NmtData2 --save-config           （迁移数据到新目录后切换）
//   DetectorConsole --load-only                                        （不检测，仅回读库中记录，验证重启不丢数据）
//   DetectorConsole --load-only --days 1                               （按保留期回读并执行一次清理）

var opts = ParseArgs(args);

var services = new ServiceCollection();
services.AddLogging(b => b
    .SetMinimumLevel(opts.Verbose ? LogLevel.Information : LogLevel.Warning)
    .AddSimpleConsole(c => c.SingleLine = true));
services.AddDetectionCore();

await using var provider = services.BuildServiceProvider();

var configService = provider.GetRequiredService<IConfigService>();
var config = configService.Load();
if (opts.OverridesDetection)
{
    // 命令行参数覆盖配置，便于无配置文件时试跑不同目标
    config.Detection.TargetIp = opts.TargetIp;
    config.Detection.Port = opts.Port;
    config.Detection.TimeoutMs = opts.TimeoutMs;
    config.Detection.PortPolicy = opts.PortPolicy;
    config.Detection.Interval = opts.Interval;
}

// 数据目录：命令行覆盖（仅本次运行生效；带 --save-config 时才写入配置，
// 正式链路由设置页保存 + IDataMigrationService 迁移，S5 联动）
if (!string.IsNullOrWhiteSpace(opts.DataDirectory))
{
    config.Storage.DataDirectory = Path.GetFullPath(opts.DataDirectory);
    AppPaths.Root = config.Storage.DataDirectory;
}

var store = provider.GetRequiredService<IStoreService>();

// 先校验再保存（DESIGN 第五节：行内提示不弹窗；控制台直接打印错误项）
if (opts.SaveConfig)
{
    var errors = configService.Validate(config);
    if (errors.Count > 0)
    {
        Console.WriteLine("配置校验未通过，未写入：");
        foreach (var error in errors)
        {
            Console.WriteLine($"  [{error.Field}] {error.Message}");
        }
        return 3;
    }

    if (!await configService.SaveAsync(config))
    {
        Console.WriteLine($"配置写入失败：{configService.ConfigFilePath}");
        return 3;
    }

    Console.WriteLine($"配置已写入：{configService.ConfigFilePath}");
}

if (opts.Migrate)
{
    await store.InitializeAsync();
    var migration = await provider.GetRequiredService<IDataMigrationService>()
        .MigrateAsync(opts.MigrateTo);
    Console.WriteLine(migration.Success
        ? $"迁移成功：{migration.FromDirectory} → {migration.ToDirectory}（{migration.CopiedFiles} 个文件）"
        : $"迁移失败：{migration.Message}（保持原目录 {migration.FromDirectory}，下方回读验证数据仍在）");

    // 失败时迁移服务已回退旧目录并重读配置；成功时已重写配置并切换
    config = configService.Current;
}

await store.InitializeAsync();

Console.WriteLine("== NetworkMonitorTool 控制台验证（S2 检测核心 + S3 存储与配置）==");
Console.WriteLine($"配置文件: {configService.ConfigFilePath}");
Console.WriteLine($"数据目录: {AppPaths.Root}");
Console.WriteLine($"数据库  : {AppPaths.DatabaseFile}（持久化 {(store.IsPersistent ? "可用" : "不可用，已降级为仅内存")}）");
Console.WriteLine($"生效配置: 目标={config.Detection.TargetIp}"
    + $" 端口={(config.Detection.Port?.ToString() ?? "无")}"
    + $" 间隔={config.Detection.Interval} 超时={config.Detection.TimeoutMs}ms"
    + $" 策略={config.Detection.PortPolicy}"
    + $" | 告警阈值={config.Alert.ConsecutiveFailureThreshold}"
    + $" | 保留={config.Storage.RetentionDays}天 内存上限={config.Storage.MaxInMemoryRecords}"
    + $" | dataDirectory={(config.Storage.DataDirectory ?? "默认")}");

if (opts.Seed)
{
    // 造两条样本：一条超出保留期的检测记录（验证时间窗过滤与清理）+ 一条告警记录（验证分表写入）
    var expired = DateTime.Now.AddDays(-(opts.Days + 2));
    var okExpired = await store.AppendDetectionRecordAsync(new DetectionRecord
    {
        Timestamp = expired,
        TargetIp = config.Detection.TargetIp,
        Port = config.Detection.Port,
        PingSuccess = false,
        LatencyMs = null,
        PortResult = PortProbeStatus.NotTested,
        Status = DetectionStatus.PingFailed,
        ErrorSummary = "Seed：超出保留期的历史样本",
        ConsecutiveFailures = 5,
    });
    var okAlert = await store.AppendAlertRecordAsync(new AlertRecord
    {
        Timestamp = DateTime.Now,
        TargetIp = config.Detection.TargetIp,
        Port = config.Detection.Port,
        ConsecutiveFailures = 3,
        ErrorSummary = "Seed：告警样本",
        DeliveryMethods = AlertDeliveryMethods.Popup | AlertDeliveryMethods.TrayNotify,
    });
    Console.WriteLine($"已写入样本：过期检测记录 {expired:yyyy-MM-dd} → {okExpired}，告警记录 → {okAlert}");
}

var since = DateTime.Now.Date.AddDays(-opts.Days);
var before = await store.LoadDetectionRecordsAsync(since);
Console.WriteLine($"回读保留期（{opts.Days} 天，{since:yyyy-MM-dd} 起）历史记录：{before.Count} 条");
if (before.Count > 0)
{
    var last = before[^1];
    Console.WriteLine($"  最近一条: [{last.Timestamp:yyyy-MM-dd HH:mm:ss}] {last.TargetIp} 状态={last.Status}"
        + $" 连续失败={last.ConsecutiveFailures}");
}
var latestCount = await store.LoadLatestConsecutiveFailuresAsync();
Console.WriteLine($"  冷启动可延续的连续失败次数: {latestCount?.ToString() ?? "无记录"}");
var alerts = await store.LoadAlertRecordsAsync(since);
Console.WriteLine($"回读告警记录（分表 alert_record）：{alerts.Count} 条"
    + (alerts.Count > 0
        ? $"，最近一条 [{alerts[^1].Timestamp:yyyy-MM-dd HH:mm:ss}] 连续失败={alerts[^1].ConsecutiveFailures}"
            + $" 送达={alerts[^1].DeliveryMethods}"
        : string.Empty));

if (opts.Clear)
{
    // 日志页“清空”的存储层等价物（S5 接入二次确认 + 界面集合）
    var clearedDetect = await store.ClearDetectionRecordsAsync();
    var clearedAlert = await store.ClearAlertRecordsAsync();
    Console.WriteLine($"已清空：检测 {clearedDetect} 条、告警 {clearedAlert} 条，"
        + $"回读 {(await store.LoadDetectionRecordsAsync(since)).Count} 条 / "
        + $"{(await store.LoadAlertRecordsAsync(since)).Count} 条");
    Console.WriteLine("--clear 模式，不执行检测。");
    return 0;
}

if (opts.LoadOnly)
{
    // 保留天数取配置值；显式给了 --days 则用它覆盖，便于验证清理边界
    var retention = opts.DaysOverridden ? opts.Days : config.Storage.RetentionDays;
    var cleanup = await provider.GetRequiredService<ICleanupService>().RunOnceAsync(retention);
    Console.WriteLine($"按保留 {retention} 天清理：截止 {cleanup.Cutoff:yyyy-MM-dd}，"
        + $"删除检测 {cleanup.RemovedDetectionRecords} 条、告警 {cleanup.RemovedAlertRecords} 条");
    Console.WriteLine($"清理后回读: {(await store.LoadDetectionRecordsAsync(since)).Count} 条");
    Console.WriteLine("--load-only 模式，不执行检测。");
    return 0;
}

var scheduler = provider.GetRequiredService<ISchedulerService>();
var options = config.Detection.ToDetectionOptions();

Console.WriteLine($"目标: {options.TargetIp}"
    + (options.HasValidPort ? $":{options.Port}" : string.Empty)
    + $" | 策略: {options.PortPolicy} | 超时: {options.TimeoutMs}ms | 间隔: {config.Detection.Interval} | 轮数: {opts.Cycles}"
    + (opts.OverridesDetection ? "（命令行覆盖配置）" : "（取配置文件）"));
Console.WriteLine(new string('-', 72));

var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
int completed = 0;
int degraded = 0;

store.WriteFailed += (_, e) =>
{
    // 与界面提示等价：写库失败不阻断检测，仅计数（DESIGN 第十四节 3）
    Interlocked.Increment(ref degraded);
    Console.WriteLine($"  ! 持久化降级（{e.Operation}）：{e.Reason}");
};

scheduler.DetectionCompleted += (_, record) =>
{
    PrintRecord(record);
    if (Interlocked.Increment(ref completed) >= opts.Cycles)
    {
        done.TrySetResult(true);
    }
};

await scheduler.StartAsync(options, config.Detection.Interval);

// 安全上限：轮数 × 间隔 + 两次超时余量，防止目标长时间无响应时挂死
var maxWait = config.Detection.Interval.ToTimeSpan() * opts.Cycles
    + TimeSpan.FromMilliseconds(options.TimeoutMs * 2L)
    + TimeSpan.FromSeconds(5);

await Task.WhenAny(done.Task, Task.Delay(maxWait));

await scheduler.StopAsync();

// 收口验证：本次写入后立即回读，再交由下一次运行（或 --load-only）验证跨进程不丢数据
var after = await store.LoadDetectionRecordsAsync(since);
Console.WriteLine(new string('-', 72));
Console.WriteLine($"共完成 {completed} 轮检测，调度状态: {scheduler.State}，写库降级 {degraded} 次。");
Console.WriteLine($"库中保留期记录: 运行前 {before.Count} 条 → 运行后 {after.Count} 条"
    + $"（新增 {after.Count - before.Count} 条，重启后可回读即 S3 收口标准）");

// 收口验证：持久化可用时本次记录必须全部入库；已降级时只要检测不中断且触发了降级提示即为预期
var persisted = after.Count - before.Count;
Console.WriteLine(store.IsPersistent
    ? $"结果: 完成 {completed} 轮、入库 {persisted} 条，跨进程回读不丢数据 ✓"
    : $"结果: 完成 {completed} 轮、入库 {persisted} 条（持久化不可用，降级 {degraded} 次）——检测未被阻断 ✓");

return completed > 0 && (store.IsPersistent ? persisted == completed : degraded > 0) ? 0 : 2;

static void PrintRecord(DetectionRecord r)
{
    var ping = r.PingSuccess ? $"成功 {r.LatencyMs}ms" : "失败";
    var port = r.PortResult switch
    {
        PortProbeStatus.Success => "成功",
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
        // 下一个参数仅在“不像开关”时才当值（PowerShell 会丢弃空串参数，
        // 否则 --migrate 会把后面的开关误当目录）
        string? Next() =>
            i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : null;
        switch (args[i].ToLowerInvariant())
        {
            case "--ip": o.TargetIp = Next() ?? o.TargetIp; o.OverridesDetection = true; break;
            case "--port": if (int.TryParse(Next(), out var p)) { o.Port = p; o.OverridesDetection = true; } break;
            case "--timeout": if (int.TryParse(Next(), out var t)) { o.TimeoutMs = t; o.OverridesDetection = true; } break;
            case "--cycles": if (int.TryParse(Next(), out var c) && c > 0) o.Cycles = c; break;
            case "--days": if (int.TryParse(Next(), out var d) && d > 0) { o.Days = d; o.DaysOverridden = true; } break;
            case "--interval-h": if (int.TryParse(Next(), out var h)) { o.Interval = o.Interval with { Hours = h }; o.OverridesDetection = true; } break;
            case "--interval-m": if (int.TryParse(Next(), out var m)) { o.Interval = o.Interval with { Minutes = m }; o.OverridesDetection = true; } break;
            case "--interval-s": if (int.TryParse(Next(), out var s)) { o.Interval = o.Interval with { Seconds = s }; o.OverridesDetection = true; } break;
            case "--policy":
                if (Enum.TryParse<PortDetectionPolicy>(Next(), ignoreCase: true, out var pol))
                {
                    o.PortPolicy = pol;
                    o.OverridesDetection = true;
                }
                break;
            case "--data-dir": o.DataDirectory = Next() ?? o.DataDirectory; break;
            case "--migrate": o.Migrate = true; o.MigrateTo = Next() ?? string.Empty; break;
            case "--save-config": o.SaveConfig = true; break;
            case "--seed": o.Seed = true; break;
            case "--load-only": o.LoadOnly = true; break;
            case "--clear": o.Clear = true; break;
            case "--verbose": o.Verbose = true; break;
        }
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
    public int Days { get; set; } = 30;

    /// <summary>是否显式给了 --days（同时用于覆盖清理保留天数）。</summary>
    public bool DaysOverridden { get; set; }
    public string? DataDirectory { get; set; }

    /// <summary>校验并写入 appsettings.json（S3 配置读写验证用）。</summary>
    public bool SaveConfig { get; set; }

    /// <summary>是否迁移数据目录（成功后切换，失败保持原目录）。</summary>
    public bool Migrate { get; set; }

    /// <summary>写入超出保留期的检测样本与告警样本，验证时间窗过滤、分表与清理。</summary>
    public bool Seed { get; set; }

    /// <summary>迁移目标目录；空字符串表示默认目录。</summary>
    public string MigrateTo { get; set; } = string.Empty;

    /// <summary>命令行是否显式给出了检测相关参数；否则沿用配置文件中的值。</summary>
    public bool OverridesDetection { get; set; }

    /// <summary>仅回读历史与执行清理，不发起检测。</summary>
    public bool LoadOnly { get; set; }

    /// <summary>清空两张记录表后退出（验证 IStoreService 清空能力）。</summary>
    public bool Clear { get; set; }

    public bool Verbose { get; set; }
}
