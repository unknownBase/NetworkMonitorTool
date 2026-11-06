using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services.Probes
{
    /// <summary>ICMP Ping 探测抽象（便于调度 / 检测服务解耦与后续测试替身）。</summary>
    public interface IPingProbe
    {
        /// <summary>对目标执行一次 Ping，返回结果（含延迟或错误摘要）。</summary>
        Task<PingProbeResult> ProbeAsync(string host, int timeoutMs, CancellationToken cancellationToken = default);
    }
}
