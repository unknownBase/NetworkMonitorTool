using System.Net.NetworkInformation;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services.Probes
{
    /// <summary>基于 <see cref="Ping"/> 的 ICMP 探测实现。</summary>
    public sealed class PingProbe : IPingProbe
    {
        public async Task<PingProbeResult> ProbeAsync(
            string host, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return PingProbeResult.Fail("目标地址为空");
            }

            try
            {
                using var ping = new Ping();
                // 32 字节默认载荷；SendPingAsync 内部按 timeoutMs 控制 ICMP 超时
                var sendTask = ping.SendPingAsync(host, timeoutMs);

                // 支持外部取消（停止调度）：与无限期延迟竞争
                if (cancellationToken.CanBeCanceled)
                {
                    var delayTask = Task.Delay(Timeout.Infinite, cancellationToken);
                    var finished = await Task.WhenAny(sendTask, delayTask).ConfigureAwait(false);
                    if (finished != sendTask)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                var reply = await sendTask.ConfigureAwait(false);

                return reply.Status switch
                {
                    IPStatus.Success => PingProbeResult.Ok(reply.RoundtripTime),
                    IPStatus.TimedOut => PingProbeResult.Fail(ProbeErrorClassifier.Timeout),
                    IPStatus.DestinationNetworkUnreachable or IPStatus.DestinationUnreachable
                        or IPStatus.TtlExpired or IPStatus.PacketTooBig
                        => PingProbeResult.Fail(ProbeErrorClassifier.Unreachable),
                    _ => PingProbeResult.Fail("ICMP " + reply.Status),
                };
            }
            catch (OperationCanceledException)
            {
                throw; // 取消交由调度处理，不算检测失败
            }
            catch (Exception ex)
            {
                return PingProbeResult.Fail(ProbeErrorClassifier.FromException(ex));
            }
        }
    }
}
