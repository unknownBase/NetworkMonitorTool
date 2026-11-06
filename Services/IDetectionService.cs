using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>连通性检测服务：编排一次 Ping + 端口检测并汇总为记录（DESIGN 第六节 2）。</summary>
    public interface IDetectionService
    {
        /// <summary>执行一次检测并返回记录。取消请求会向上抛出 <see cref="OperationCanceledException"/>。</summary>
        Task<DetectionRecord> DetectOnceAsync(DetectionOptions options, CancellationToken cancellationToken = default);
    }
}
