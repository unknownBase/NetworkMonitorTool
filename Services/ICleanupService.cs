namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 过期记录清理（DESIGN 第十一节 2）：按保留天数删除检测与告警历史，
    /// 启动时执行一次，运行期间按 <see cref="Interval"/> 定时执行。
    /// </summary>
    public interface ICleanupService
    {
        /// <summary>定时清理间隔，默认 1 小时。</summary>
        TimeSpan Interval { get; }

        /// <summary>
        /// 立即执行一次清理，保留天数取 <see cref="IConfigService.Current"/> 的 storage.retentionDays。
        /// </summary>
        Task<CleanupResult> RunOnceAsync(CancellationToken cancellationToken = default);

        /// <summary>按指定保留天数执行一次清理（设置页保存后即时按新值执行，DESIGN 第五节 2）。</summary>
        Task<CleanupResult> RunOnceAsync(int retentionDays, CancellationToken cancellationToken = default);
    }

    /// <summary>一次清理的结果统计（截止时刻与两张表各删除条数）。</summary>
    public sealed record CleanupResult(DateTime Cutoff, int RemovedDetectionRecords, int RemovedAlertRecords);
}
