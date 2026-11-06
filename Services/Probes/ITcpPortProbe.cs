using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services.Probes
{
    /// <summary>TCP 端口连通性探测抽象。</summary>
    public interface ITcpPortProbe
    {
        /// <summary>尝试连接 host:port，返回三态结果（含连接耗时或错误摘要）。</summary>
        Task<PortProbeResult> ProbeAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken = default);
    }
}
