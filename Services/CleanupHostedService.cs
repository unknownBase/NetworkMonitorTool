using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 清理后台服务：启动后先执行一次，之后按 <see cref="ICleanupService.Interval"/>（默认每小时）定时执行
    /// （DESIGN 第十一节 2）。单次失败不终止循环，仅记录日志。
    /// </summary>
    public sealed class CleanupHostedService : BackgroundService
    {
        private readonly ICleanupService _cleanup;
        private readonly ILogger<CleanupHostedService>? _logger;

        public CleanupHostedService(ICleanupService cleanup, ILogger<CleanupHostedService>? logger = null)
        {
            _cleanup = cleanup;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _cleanup.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                    _logger?.LogInformation(
                        "定时清理完成：截止 {Cutoff:yyyy-MM-dd}，删除检测 {Detect} 条、告警 {Alert} 条",
                        result.Cutoff, result.RemovedDetectionRecords, result.RemovedAlertRecords);
                }
                catch (OperationCanceledException)
                {
                    break; // 正常停机
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "定时清理执行失败，将在下一周期重试");
                }

                try
                {
                    await Task.Delay(_cleanup.Interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
