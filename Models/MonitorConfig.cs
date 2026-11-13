using System.Text.Json.Serialization;

namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 顶层监测配置（DESIGN 第五节配置持久化 / 第四节设置页六组分区）。
    /// 序列化到固定位置的 appsettings.json，缺失字段取默认值（<see cref="CreateDefault"/>）。
    /// 配置文件含版本号 <see cref="ConfigVersion"/>；新增可选字段属向后兼容（旧文件缺字段取默认），
    /// 无需递增版本，仅在字段语义变更 / 重命名或迁移时递增。
    /// v2（S16）新增多目标列表，旧的 detection.targetIp / port 仅作迁移输入（见 DESIGN_S16_MULTITARGET 第四节）。
    /// </summary>
    public sealed class MonitorConfig
    {
        /// <summary>当前配置文件结构版本；结构变更时递增并做兼容迁移。</summary>
        public const int CurrentVersion = 2;

        /// <summary>配置文件结构版本（缺失时按 1 处理）。</summary>
        public int ConfigVersion { get; set; } = CurrentVersion;

        /// <summary>a) 目标列表（S16 多目标；至少保留 1 个）。</summary>
        public List<TargetConfig> Targets { get; set; } = new();

        /// <summary>b) 检测设置（全局共享的间隔 / 超时 / 端口策略）。</summary>
        public DetectionConfig Detection { get; set; } = new();

        /// <summary>c) 告警设置（S6 告警引擎消费）。</summary>
        public AlertConfig Alert { get; set; } = new();

        /// <summary>d) 存储设置。</summary>
        public StorageConfig Storage { get; set; } = new();

        /// <summary>e) 界面设置（关闭行为 / 工具条开关，S8、S9 消费）。</summary>
        public InterfaceConfig Interface { get; set; } = new();

        /// <summary>f) 启动设置（开机自启，S10 消费）。</summary>
        public StartupConfig Startup { get; set; } = new();

        /// <summary>恢复默认配置（设置页“恢复默认”按钮，S4）：一个回环地址目标。</summary>
        public static MonitorConfig CreateDefault() => new()
        {
            Targets = new List<TargetConfig>
            {
                new() { Id = TargetConfig.LegacyTargetId },
            },
        };

        /// <summary>是否存在启用目标（全部禁用时禁止启动调度，DESIGN_S16 第六节）。</summary>
        [JsonIgnore]
        public bool HasEnabledTarget => Targets.Any(t => t.Enabled);

        /// <summary>按 Id 查找目标；不存在（如已被删除）时返回 null。</summary>
        public TargetConfig? FindTarget(string? targetId)
            => string.IsNullOrEmpty(targetId)
                ? null
                : Targets.FirstOrDefault(t => string.Equals(t.Id, targetId, StringComparison.Ordinal));

        /// <summary>目标展示名：找不到目标时回退为目标 Id 本身（历史记录仍可辨认）。</summary>
        public string GetTargetDisplayName(string? targetId)
        {
            var target = FindTarget(targetId);
            return target?.DisplayName
                ?? (string.IsNullOrEmpty(targetId) ? "未知目标" : targetId);
        }

        /// <summary>派生启用目标的检测参数（调度启动时一次性快照）。</summary>
        public IReadOnlyList<DetectionOptions> GetEnabledDetectionOptions()
            => Targets
                .Where(t => t.Enabled)
                .Select(t => t.ToDetectionOptions(Detection.TimeoutMs, Detection.PortPolicy))
                .ToList();

        /// <summary>
        /// 保留期起始时刻（本地日期零时）：回读历史、界面时间窗与清理均以此为界（DESIGN 第十一节 2）。
        /// 非法（≤ 0）保留天数回退默认值，避免一次性清空或全量加载。派生值不入配置文件。
        /// </summary>
        [JsonIgnore]
        public DateTime RetentionStart
        {
            get
            {
                var days = Storage.RetentionDays > 0
                    ? Storage.RetentionDays
                    : CreateDefault().Storage.RetentionDays;
                return DateTime.Now.Date.AddDays(-days);
            }
        }

        /// <summary>
        /// 深拷贝一份（设置页编辑草稿，避免未保存的修改污染 <see cref="Services.IConfigService.Current"/>）。
        /// </summary>
        public MonitorConfig Clone() => new()
        {
            ConfigVersion = ConfigVersion,
            Targets = Targets.Select(t => t.Clone()).ToList(),
            Detection = Detection.Clone(),
            Alert = Alert.Clone(),
            Storage = Storage.Clone(),
            Interface = Interface.Clone(),
            Startup = Startup.Clone(),
        };
    }

    /// <summary>
    /// 检测相关配置（全局共享的间隔、超时、端口策略）。
    /// <see cref="LegacyTargetIp"/> / <see cref="LegacyPort"/> 只用于 v1 → v2 迁移读取，迁移后置空且不再写出。
    /// </summary>
    public sealed class DetectionConfig
    {
        /// <summary>检测间隔（时/分/秒），默认 60 秒。</summary>
        public DetectionInterval Interval { get; set; } = new(0, 1, 0);

        /// <summary>连接 / 发送超时（毫秒）。</summary>
        public int TimeoutMs { get; set; } = 3000;

        /// <summary>端口检测策略。</summary>
        public PortDetectionPolicy PortPolicy { get; set; } = PortDetectionPolicy.OnlyWhenPingSuccess;

        /// <summary>v1 单目标地址：仅迁移输入，迁移完成后置空（不写出配置文件）。</summary>
        [JsonPropertyName("targetIp")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LegacyTargetIp { get; set; }

        /// <summary>v1 单目标端口：仅迁移输入，迁移完成后置空（不写出配置文件）。</summary>
        [JsonPropertyName("port")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? LegacyPort { get; set; }

        public DetectionConfig Clone() => new()
        {
            Interval = Interval,
            TimeoutMs = TimeoutMs,
            PortPolicy = PortPolicy,
            LegacyTargetIp = LegacyTargetIp,
            LegacyPort = LegacyPort,
        };
    }

    /// <summary>告警设置（DESIGN 第八节；S6 告警引擎落地计数与触发逻辑）。</summary>
    public sealed class AlertConfig
    {
        /// <summary>连续失败告警阈值，默认 3。</summary>
        public int ConsecutiveFailureThreshold { get; set; } = 3;

        /// <summary>是否将“端口失败”单独计入连续失败（默认计入）。</summary>
        public bool CountPortFailure { get; set; } = true;

        /// <summary>是否应用内弹窗告警。</summary>
        public bool PopupEnabled { get; set; } = true;

        /// <summary>是否托盘通知告警。</summary>
        public bool TrayNotifyEnabled { get; set; } = true;

        public AlertConfig Clone() => new()
        {
            ConsecutiveFailureThreshold = ConsecutiveFailureThreshold,
            CountPortFailure = CountPortFailure,
            PopupEnabled = PopupEnabled,
            TrayNotifyEnabled = TrayNotifyEnabled,
        };
    }

    /// <summary>存储设置（DESIGN 第十一节持久化与保留策略）。</summary>
    public sealed class StorageConfig
    {
        /// <summary>历史保留天数，默认 30。</summary>
        public int RetentionDays { get; set; } = 30;

        /// <summary>界面内存集合最大条数（S5 日志页消费），默认 5000。</summary>
        public int MaxInMemoryRecords { get; set; } = 5000;

        /// <summary>自定义数据目录；空表示使用默认 %LOCALAPPDATA%\NetworkMonitorTool。</summary>
        public string? DataDirectory { get; set; }

        public StorageConfig Clone() => new()
        {
            RetentionDays = RetentionDays,
            MaxInMemoryRecords = MaxInMemoryRecords,
            DataDirectory = DataDirectory,
        };
    }
}
