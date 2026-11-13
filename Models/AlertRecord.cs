namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 告警送达通道（DESIGN 第四节告警历史页“告警方式”字段；S14 扩展邮件 / Webhook）。
    /// 位标志，一次告警可同时经多通道送达。
    /// </summary>
    [Flags]
    public enum AlertDeliveryMethods
    {
        /// <summary>无（异常降级或通道全部关闭）。</summary>
        None = 0,

        /// <summary>应用内弹窗。</summary>
        Popup = 1,

        /// <summary>托盘通知。</summary>
        TrayNotify = 2,
    }

    /// <summary>
    /// 单条告警记录（对应 DESIGN 第四节告警历史页字段），
    /// S3 迭代持久化到 SQLite alert_record 表（与检测记录分表）。
    /// 写入逻辑由 S6 告警引擎触发。
    /// </summary>
    public sealed class AlertRecord
    {
        /// <summary>告警触发时间（本地时间）。</summary>
        public required DateTime Timestamp { get; init; }

        /// <summary>目标 IP 或主机名。</summary>
        public required string TargetIp { get; init; }

        /// <summary>目标标识（S16 多目标）：对应 <see cref="TargetConfig.Id"/>，旧记录为迁移目标 default。</summary>
        public string TargetId { get; init; } = string.Empty;

        /// <summary>目标端口，未配置端口时为 null。</summary>
        public int? Port { get; init; }

        /// <summary>触发时的连续失败次数。</summary>
        public required int ConsecutiveFailures { get; init; }

        /// <summary>最近失败原因摘要。</summary>
        public string? ErrorSummary { get; init; }

        /// <summary>
        /// 触发告警的最近一次检测失败时间（弹窗内容用，DESIGN 第八节 5）。
        /// 立即告警（启动时达阈）与触发时间可能不同；旧数据无此字段时为 null。
        /// </summary>
        public DateTime? LastFailureAt { get; init; }

        /// <summary>实际送达的告警通道。</summary>
        public AlertDeliveryMethods DeliveryMethods { get; init; } = AlertDeliveryMethods.None;
    }
}
