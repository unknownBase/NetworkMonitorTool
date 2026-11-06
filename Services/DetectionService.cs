using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services.Probes;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 默认检测编排服务：Ping → 按端口策略决定是否检测端口 → 汇总整体状态与错误摘要。
    /// 只负责“检测与判定”，内存集合、持久化、告警交由上层在收到记录后处理（DESIGN 第六节 2）。
    /// </summary>
    public sealed class DetectionService : IDetectionService
    {
        private readonly IPingProbe _pingProbe;
        private readonly ITcpPortProbe _portProbe;
        private readonly ILogger<DetectionService>? _logger;

        public DetectionService(
            IPingProbe pingProbe,
            ITcpPortProbe portProbe,
            ILogger<DetectionService>? logger = null)
        {
            _pingProbe = pingProbe;
            _portProbe = portProbe;
            _logger = logger;
        }

        public async Task<DetectionRecord> DetectOnceAsync(
            DetectionOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            try
            {
                var ping = await _pingProbe.ProbeAsync(options.TargetIp, options.TimeoutMs, cancellationToken)
                    .ConfigureAwait(false);

                var port = await ProbePortIfNeededAsync(options, ping.Success, cancellationToken)
                    .ConfigureAwait(false);

                var status = DetermineStatus(ping.Success, port.Status);

                return new DetectionRecord
                {
                    Timestamp = DateTime.Now,
                    TargetIp = options.TargetIp,
                    Port = options.HasValidPort ? options.Port : null,
                    PingSuccess = ping.Success,
                    LatencyMs = ping.LatencyMs,
                    PortResult = port.Status,
                    Status = status,
                    ErrorSummary = BuildErrorSummary(status, ping, port),
                };
            }
            catch (OperationCanceledException)
            {
                throw; // 停止 / 取消由调度器处理，不记为异常
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "检测 {Target} 时发生未预期异常", options.TargetIp);
                return new DetectionRecord
                {
                    Timestamp = DateTime.Now,
                    TargetIp = options.TargetIp,
                    Port = options.HasValidPort ? options.Port : null,
                    PingSuccess = false,
                    LatencyMs = null,
                    PortResult = PortProbeStatus.NotTested,
                    Status = DetectionStatus.Error,
                    ErrorSummary = ProbeErrorClassifier.FromException(ex),
                };
            }
        }

        /// <summary>端口策略四分支判定（DESIGN 第七节）。</summary>
        private async Task<PortProbeResult> ProbePortIfNeededAsync(
            DetectionOptions options, bool pingSuccess, CancellationToken cancellationToken)
        {
            if (!options.HasValidPort)
            {
                return PortProbeResult.NotTested(); // 未配置端口
            }

            bool shouldProbe = options.PortPolicy switch
            {
                PortDetectionPolicy.Always => true,
                PortDetectionPolicy.OnlyWhenPingSuccess => pingSuccess,
                PortDetectionPolicy.OnlyWhenPingFailed => !pingSuccess,
                PortDetectionPolicy.Never => false,
                _ => false,
            };

            if (!shouldProbe)
            {
                return PortProbeResult.NotTested();
            }

            return await _portProbe
                .ProbeAsync(options.TargetIp, options.Port!.Value, options.TimeoutMs, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 整体状态优先级：异常 &gt; Ping失败 &gt; 端口失败 &gt; 成功。
        /// “仅 Ping 失败时检测”命中且端口成功时，状态仍记 PingFailed，
        /// 但端口结果字段体现为成功，供界面据此判断 ICMP 是否被禁（DESIGN 第七节）。
        /// </summary>
        private static DetectionStatus DetermineStatus(bool pingSuccess, PortProbeStatus portStatus)
        {
            if (!pingSuccess)
            {
                return DetectionStatus.PingFailed;
            }

            if (portStatus == PortProbeStatus.Failed)
            {
                return DetectionStatus.PortFailed;
            }

            return DetectionStatus.Success;
        }

        private static string? BuildErrorSummary(
            DetectionStatus status, PingProbeResult ping, PortProbeResult port)
        {
            if (status == DetectionStatus.Success)
            {
                return null;
            }

            var parts = new List<string>(2);
            if (!ping.Success && ping.ErrorSummary is not null)
            {
                parts.Add("Ping：" + ping.ErrorSummary);
            }

            if (port.Status == PortProbeStatus.Failed && port.ErrorSummary is not null)
            {
                parts.Add("端口：" + port.ErrorSummary);
            }

            return parts.Count > 0 ? string.Join("；", parts) : status.ToString();
        }
    }
}
