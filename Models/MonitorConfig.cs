using System.Text.Json.Serialization;

namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 顶层监测配置（DESIGN 第五节配置持久化 / 第四节设置页六组分区）。
    /// 序列化到固定位置的 appsettings.json，缺失字段取默认值（<see cref="CreateDefault"/>）。
    /// 配置文件含版本号 <see cref="ConfigVersion"/>；新增可选字段属向后兼容（旧文件缺字段取默认），
    /// 无需递增版本，仅在字段语义变更 / 重命名或迁移时递增。
    /// </summary>
    public sealed class MonitorConfig
    {
        /// <summary>当前配置文件结构版本；结构变更时递增并做兼容迁移。</summary>
        public const int CurrentVersion = 1;

        /// <summary>配置文件结构版本（缺失时按 1 处理）。</summary>
        public int ConfigVersion { get; set; } = CurrentVersion;

        /// <summary>a) 目标 + b) 检测设置。</summary>
        public DetectionConfig Detection { get; set; } = new();

        /// <summary>c) 告警设置（S6 告警引擎消费）。</summary>
        public AlertConfig Alert { get; set; } = new();

        /// <summary>d) 存储设置。</summary>
        public StorageConfig Storage { get; set; } = new();

        /// <summary>e) 界面设置（关闭行为 / 工具条开关，S8、S9 消费）。</summary>
        public InterfaceConfig Interface { get; set; } = new();

        /// <summary>f) 启动设置（开机自启，S10 消费）。</summary>
        public StartupConfig Startup { get; set; } = new();

        /// <summary>恢复默认配置（设置页“恢复默认”按钮，S4）。</summary>
        public static MonitorConfig CreateDefault() => new();

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
            Detection = Detection.Clone(),
            Alert = Alert.Clone(),
            Storage = Storage.Clone(),
            Interface = Interface.Clone(),
            Startup = Startup.Clone(),
        };
    }

    /// <summary>
    /// 检测相关配置（目标、端口、间隔、超时、端口策略），
    /// 通过 <see cref="ToDetectionOptions"/> 派生 S2 检测核心所需的 <see cref="DetectionOptions"/>。
    /// </summary>
    public sealed class DetectionConfig
    {
        /// <summary>目标 IP 或主机名，默认本机回环。</summary>
        public string TargetIp { get; set; } = "127.0.0.1";

        /// <summary>目标端口，null 表示不检测端口。</summary>
        public int? Port { get; set; }

        /// <summary>检测间隔（时/分/秒），默认 60 秒。</summary>
        public DetectionInterval Interval { get; set; } = new(0, 1, 0);

        /// <summary>连接 / 发送超时（毫秒）。</summary>
        public int TimeoutMs { get; set; } = 3000;

        /// <summary>端口检测策略。</summary>
        public PortDetectionPolicy PortPolicy { get; set; } = PortDetectionPolicy.OnlyWhenPingSuccess;

        /// <summary>派生检测核心参数（DESIGN 第五节：保存后按新配置重启调度周期）。</summary>
        public DetectionOptions ToDetectionOptions() => new()
        {
            TargetIp = TargetIp,
            Port = Port,
            TimeoutMs = TimeoutMs,
            PortPolicy = PortPolicy,
        };

        public DetectionConfig Clone() => new()
        {
            TargetIp = TargetIp,
            Port = Port,
            Interval = Interval,
            TimeoutMs = TimeoutMs,
            PortPolicy = PortPolicy,
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
