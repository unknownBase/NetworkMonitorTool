using NetworkMonitorTool.Models;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 按保留天数清理过期记录：检测记录与告警记录共用保留期（DESIGN 第十一节 2，
    /// 告警单独保留天数为 S12 可选增强）。
    /// </summary>
    public sealed class CleanupService : ICleanupService
    {
        /// <summary>定时清理间隔（DESIGN 建议每小时一次）。</summary>
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

        private readonly IStoreService _store;
        private readonly IConfigService _config;
        private readonly ILogger<CleanupService>? _logger;

        public CleanupService(
            IStoreService store,
            IConfigService config,
            ILogger<CleanupService>? logger = null)
        {
            _store = store;
            _config = config;
            _logger = logger;
        }

        public TimeSpan Interval => DefaultInterval;

        public Task<CleanupResult> RunOnceAsync(CancellationToken cancellationToken = default)
            => RunOnceAsync(_config.Current.Storage.RetentionDays, cancellationToken);

        public async Task<CleanupResult> RunOnceAsync(
            int retentionDays, CancellationToken cancellationToken = default)
        {
            if (retentionDays <= 0)
            {
                // 非法保留天数不删数据，按默认值执行（DESIGN 第五节校验已在保存时拦截）
                retentionDays = MonitorConfig.CreateDefault().Storage.RetentionDays;
            }

            // 以自然日为界，与日志页“早于某日期的记录将被自动清理”提示一致
            var cutoff = DateTime.Now.Date.AddDays(-retentionDays);

            var detect = await _store.DeleteDetectionRecordsBeforeAsync(cutoff, cancellationToken)
                .ConfigureAwait(false);
            var alert = await _store.DeleteAlertRecordsBeforeAsync(cutoff, cancellationToken)
                .ConfigureAwait(false);

            if (detect > 0 || alert > 0)
            {
                _logger?.LogInformation(
                    "已清理 {Cutoff:yyyy-MM-dd} 之前的记录：检测 {Detect} 条、告警 {Alert} 条",
                    cutoff, detect, alert);
            }

            return new CleanupResult(cutoff, detect, alert);
        }
    }
}
