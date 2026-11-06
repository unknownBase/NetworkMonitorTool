using System.Diagnostics;
using System.Net.Sockets;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services.Probes
{
    /// <summary>基于 <see cref="TcpClient"/> 的 TCP 端口探测实现。</summary>
    public sealed class TcpPortProbe : ITcpPortProbe
    {
        public async Task<PortProbeResult> ProbeAsync(
            string host, int port, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return PortProbeResult.Fail("目标地址为空");
            }

            if (port is < 1 or > 65535)
            {
                return PortProbeResult.Fail("端口非法");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs > 0 ? timeoutMs : Timeout.Infinite);

            var sw = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
                sw.Stop();
                return PortProbeResult.Ok(sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                // 外部取消（停止调度）向上抛出；仅超时则记为失败
                cancellationToken.ThrowIfCancellationRequested();
                return PortProbeResult.Fail(ProbeErrorClassifier.Timeout);
            }
            catch (SocketException ex)
            {
                return PortProbeResult.Fail(ex.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => ProbeErrorClassifier.Refused,
                    SocketError.TimedOut => ProbeErrorClassifier.Timeout,
                    SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData
                        => ProbeErrorClassifier.DnsFailure,
                    SocketError.NetworkUnreachable or SocketError.HostUnreachable
                        or SocketError.NetworkDown or SocketError.ConnectionReset
                        => ProbeErrorClassifier.Unreachable,
                    SocketError.AccessDenied => ProbeErrorClassifier.AccessDenied,
                    _ => ProbeErrorClassifier.FromException(ex),
                });
            }
            catch (Exception ex)
            {
                return PortProbeResult.Fail(ProbeErrorClassifier.FromException(ex));
            }
        }
    }
}
