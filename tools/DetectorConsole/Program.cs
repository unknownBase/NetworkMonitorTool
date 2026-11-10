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
//   DetectorConsole --export D:\Temp\log.csv                           （S5：导出保留期记录为 CSV 并验证 BOM/转义）
//   DetectorConsole --ip 10.255.255.1 --timeout 1500 --cycles 5        （S6：断网达阈值恰好告警一次）
//   DetectorConsole --threshold 2 --count-port off --port 9 --policy Always --cycles 3
//                                                                      （S6：阈值与端口失败计入开关覆盖，仅本次运行）
//   DetectorConsole --export-alerts D:\Temp\alerts.csv                 （S7：导出告警历史并验证 BOM/转义/最近失败时间）
//   DetectorConsole --clamp-check                                    （S8：工具条坐标越界校正纯函数回归）

var opts = ParseArgs(args);

if (opts.ClampCheck)
{
    // S8 收口验证：WindowModeService.ClampPosition 纯函数（不触界面），覆盖多显示器负原点与窗口超屏
    var ok = true;
    void Check(string name, (double Left, double Top) actual, double left, double top)
    {
        var pass = Math.Abs(actual.Left - left) < 1e-9 && Math.Abs(actual.Top - top) < 1e-9;
        ok &= pass;
        Console.WriteLine($"  {(pass ? "✓" : "✗")} {name}: ({actual.Left}, {actual.Top})");
    }

    Console.WriteLine("== 工具条坐标越界校正回归（屏幕矩形内夹取，窗口 680x46）==");
    Check("范围内保持", WindowModeService.ClampPosition(100, 100, 680, 46, 0, 0, 1920, 1080), 100, 100);
    Check("右下溢出校正", WindowModeService.ClampPosition(1800, 1070, 680, 46, 0, 0, 1920, 1080), 1240, 1034);
    Check("负坐标校正", WindowModeService.ClampPosition(-50, -20, 680, 46, 0, 0, 1920, 1080), 0, 0);
    Check("副屏负原点保持", WindowModeService.ClampPosition(-1000, 100, 680, 46, -1920, 0, 3840, 1080), -1000, 100);
    Check("窗口大于屏幕贴原点", WindowModeService.ClampPosition(500, 500, 2000, 1200, 0, 0, 1920, 1080), 0, 0);
    Console.WriteLine($"结果: {(ok ? "全部通过" : "存在失败")}");
    return ok ? 0 : 2;
}

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

// 告警参数覆盖（仅本次运行生效，不写配置）：验证阈值与端口失败计入开关
if (opts.ThresholdOverride is int threshold)
{
    config.Alert.ConsecutiveFailureThreshold = threshold;
}
if (opts.CountPortOverride is bool countPort)
{
    config.Alert.CountPortFailure = countPort;
}

// 数据目录：命令行覆盖（仅本次运行生效；带 --save-config 时才写入配置，
// 正式链路由设置页保存 + IDataMigrationService 迁移，S4/S5 已落地）
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

// S6：冷启动延续（与应用启动同序），并打印告警触发供回归核对
var alertEngine = provider.GetRequiredService<IAlertService>();
await alertEngine.InitializeAsync();
alertEngine.AlertTriggered += (_, a) => Console.WriteLine(
    $"  ! 告警触发 [{a.Timestamp:HH:mm:ss}] {a.TargetIp}:{a.Port?.ToString() ?? "-"}"
    + $" 连续失败={a.ConsecutiveFailures} 最近失败=[{a.LastFailureAt?.ToString("HH:mm:ss") ?? "无"}]"
    + $" 送达={a.DeliveryMethods} 原因={a.ErrorSummary ?? "无"}");

Console.WriteLine("== NetworkMonitorTool 控制台验证（S2 检测核心 + S3 存储与配置 + S5 导出 + S6/S7 告警）==");
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
            + $" 最近失败时间={(alerts[^1].LastFailureAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "无（旧数据）")}"
            + $" 送达={alerts[^1].DeliveryMethods}"
        : string.Empty));

if (opts.ExportTo is { Length: > 0 } exportTo)
{
    // S5 收口验证：导出保留期记录（日志页“当前筛选结果”的全集等价物）+ BOM 与字段转义检查
    var export = provider.GetRequiredService<IExportService>();
    var exported = await export.ExportDetectionRecordsAsync(before, exportTo);
    var bytes = await File.ReadAllBytesAsync(exportTo);
    var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    Console.WriteLine($"导出 {exported} 条 → {exportTo}（UTF-8 BOM {(hasBom ? "有 ✓" : "缺失 ✗")}）");
    Console.WriteLine($"默认文件名示例: {export.BuildDefaultFileName(config.Detection.TargetIp)}");

    var escapePath = Path.ChangeExtension(exportTo, ".escape.csv");
    var tricky = new DetectionRecord
    {
        Timestamp = DateTime.Now,
        TargetIp = config.Detection.TargetIp,
        PingSuccess = false,
        Status = DetectionStatus.Error,
        ErrorSummary = "a,\"b\"\nc",
    };
    await export.ExportDetectionRecordsAsync(new[] { tricky }, escapePath);
    var escaped = (await File.ReadAllTextAsync(escapePath)).Contains("\"a,\"\"b\"\"\nc\"");
    Console.WriteLine($"字段转义验证（逗号/引号/换行）: {(escaped ? "✓" : "✗")} → {escapePath}");
    Console.WriteLine("--export 模式，不执行检测。");
    return hasBom && escaped && exported == before.Count ? 0 : 2;
}

if (opts.ExportAlertsTo is { Length: > 0 } exportAlertsTo)
{
    // S7 收口验证：导出保留期告警记录 + BOM / 字段转义 / 最近失败时间列检查
    var export = provider.GetRequiredService<IExportService>();
    var exported = await export.ExportAlertRecordsAsync(alerts, exportAlertsTo);
    var bytes = await File.ReadAllBytesAsync(exportAlertsTo);
    var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    Console.WriteLine($"导出告警 {exported} 条 → {exportAlertsTo}（UTF-8 BOM {(hasBom ? "有 ✓" : "缺失 ✗")}）");
    Console.WriteLine($"默认文件名示例: {export.BuildAlertDefaultFileName(config.Detection.TargetIp)}");

    var escapePath = Path.ChangeExtension(exportAlertsTo, ".escape.csv");
    var tricky = new AlertRecord
    {
        Timestamp = DateTime.Now,
        TargetIp = config.Detection.TargetIp,
        ConsecutiveFailures = 3,
        LastFailureAt = DateTime.Now.AddSeconds(-1),
        ErrorSummary = "a,\"b\"\nc",
        DeliveryMethods = AlertDeliveryMethods.Popup | AlertDeliveryMethods.TrayNotify,
    };
    await export.ExportAlertRecordsAsync(new[] { tricky }, escapePath);
    var escapedText = await File.ReadAllTextAsync(escapePath);
    var escaped = escapedText.Contains("\"a,\"\"b\"\"\nc\"") && escapedText.Contains("弹窗 + 托盘通知");
    Console.WriteLine($"字段转义与告警方式文案验证: {(escaped ? "✓" : "✗")} → {escapePath}");
    Console.WriteLine("--export-alerts 模式，不执行检测。");
    return hasBom && escaped && exported == alerts.Count ? 0 : 2;
}

if (opts.Clear)
{
    // 日志页“清空”的存储层等价物（界面侧二次确认 + 集合清空已在 S5 接入）
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
var alertsAfter = await store.LoadAlertRecordsAsync(since);
Console.WriteLine($"告警引擎: 连续失败={alertEngine.ConsecutiveFailures} 已告警={alertEngine.IsAlerting}；"
    + $"告警历史 {alerts.Count} → {alertsAfter.Count} 条（新增 {alertsAfter.Count - alerts.Count} 条）");

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
        $"Ping={ping} | 端口={port} | 状态={r.Status} | 连续失败={r.ConsecutiveFailures}" +
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
            case "--export": o.ExportTo = Next(); break;
            case "--export-alerts": o.ExportAlertsTo = Next(); break;
            case "--threshold": if (int.TryParse(Next(), out var th) && th > 0) o.ThresholdOverride = th; break;
            case "--count-port":
                var cp = Next();
                if (cp is "on" or "true") o.CountPortOverride = true;
                else if (cp is "off" or "false") o.CountPortOverride = false;
                break;
            case "--clear": o.Clear = true; break;
            case "--clamp-check": o.ClampCheck = true; break;
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

    /// <summary>导出保留期检测记录到该 CSV 路径后退出（S5 导出验证）。</summary>
    public string? ExportTo { get; set; }

    /// <summary>导出保留期告警记录到该 CSV 路径后退出（S7 告警导出验证）。</summary>
    public string? ExportAlertsTo { get; set; }

    /// <summary>告警阈值覆盖（仅本次运行，S6 验证）。</summary>
    public int? ThresholdOverride { get; set; }

    /// <summary>端口失败计入开关覆盖（仅本次运行，S6 验证）。</summary>
    public bool? CountPortOverride { get; set; }

    /// <summary>工具条坐标越界校正纯函数回归后退出（S8 验证，不触界面）。</summary>
    public bool ClampCheck { get; set; }

    public bool Verbose { get; set; }
}
