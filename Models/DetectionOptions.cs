namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 单次检测所需参数（目标 + 超时 + 端口策略）。
    /// S3 迭代的 <c>MonitorConfig</c> 将包含并派生本对象；此处保持检测核心与配置持久化解耦。
    /// </summary>
    public sealed class DetectionOptions
    {
        /// <summary>目标标识（S16 多目标）：随记录写入并经告警引擎按目标分账。</summary>
        public string TargetId { get; init; } = string.Empty;

        /// <summary>目标 IP 或主机名。</summary>
        public required string TargetIp { get; init; }

        /// <summary>目标端口，null 表示不检测端口。</summary>
        public int? Port { get; init; }

        /// <summary>连接 / 发送超时（毫秒），默认 3000。</summary>
        public int TimeoutMs { get; init; } = 3000;

        /// <summary>端口检测策略。</summary>
        public PortDetectionPolicy PortPolicy { get; init; } = PortDetectionPolicy.OnlyWhenPingSuccess;

        /// <summary>端口合法性（1~65535）。</summary>
        public bool HasValidPort => Port is >= 1 and <= 65535;
    }
}
