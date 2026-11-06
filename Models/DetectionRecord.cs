namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 单条检测记录（对应 DESIGN 第四节日志页字段与第十二节 CSV 导出列）。
    /// S3 迭代将其持久化到 SQLite detect_record 表。
    /// </summary>
    public sealed class DetectionRecord
    {
        /// <summary>检测完成时间（本地时间）。</summary>
        public required DateTime Timestamp { get; init; }

        /// <summary>目标 IP 或主机名。</summary>
        public required string TargetIp { get; init; }

        /// <summary>本次是否执行了端口检测（配置端口且按策略命中）。</summary>
        public int? Port { get; init; }

        /// <summary>Ping 是否成功。</summary>
        public required bool PingSuccess { get; init; }

        /// <summary>Ping 延迟（毫秒），失败时为 null。</summary>
        public long? LatencyMs { get; init; }

        /// <summary>端口结果三态。</summary>
        public PortProbeStatus PortResult { get; init; } = PortProbeStatus.NotTested;

        /// <summary>整体状态（不含暂停中 / 告警中这两种界面状态）。</summary>
        public required DetectionStatus Status { get; init; }

        /// <summary>错误摘要（区分超时 / 不可达 / 权限等），成功时为 null。</summary>
        public string? ErrorSummary { get; init; }

        /// <summary>
        /// 本次检测后的连续失败计数（成功时为 0），由 S6 告警引擎填写并持久化，
        /// 供冷启动延续读取（DESIGN 第八节 3）。S2/S3 阶段默认 0。
        /// </summary>
        public int ConsecutiveFailures { get; init; }
    }
}
