namespace NetworkMonitorTool.Models
{
    /// <summary>Ping 探测结果（错误摘要区分超时 / 不可达 / 权限等，见 DESIGN 第十四节第 2 条）。</summary>
    /// <param name="Success">是否收到 ICMP 回包。</param>
    /// <param name="LatencyMs">往返延迟（毫秒），失败时为 null。</param>
    /// <param name="ErrorSummary">失败原因摘要，成功时为 null。</param>
    public sealed record PingProbeResult(bool Success, long? LatencyMs, string? ErrorSummary)
    {
        public static PingProbeResult Ok(long latencyMs) => new(true, latencyMs, null);

        public static PingProbeResult Fail(string errorSummary) => new(false, null, errorSummary);
    }

    /// <summary>TCP 端口探测结果（三态 + 错误摘要）。</summary>
    /// <param name="Status">成功 / 失败 / 未检测。</param>
    /// <param name="LatencyMs">连接耗时（毫秒），非成功时为 null。</param>
    /// <param name="ErrorSummary">失败原因摘要，成功或未检测时为 null。</param>
    public sealed record PortProbeResult(PortProbeStatus Status, long? LatencyMs, string? ErrorSummary)
    {
        public static PortProbeResult Ok(long latencyMs) => new(PortProbeStatus.Success, latencyMs, null);

        public static PortProbeResult Fail(string errorSummary) => new(PortProbeStatus.Failed, null, errorSummary);

        public static PortProbeResult NotTested(string? reason = null) => new(PortProbeStatus.NotTested, null, reason);
    }
}
