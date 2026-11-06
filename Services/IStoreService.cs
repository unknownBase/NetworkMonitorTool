using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 检测 / 告警记录持久化（DESIGN 第十一节）。
    /// 写入失败不抛异常中断业务：降级为仅内存并触发 <see cref="WriteFailed"/> 供界面提示
    /// （DESIGN 第十四节 3）。
    /// </summary>
    public interface IStoreService
    {
        /// <summary>
        /// 当前是否处于持久化可用状态。false 表示已降级为仅内存，
        /// 界面据此提示“持久化异常”（DESIGN 第十四节 3）。
        /// </summary>
        bool IsPersistent { get; }

        /// <summary>写入失败（已降级）时触发，参数为失败原因摘要。</summary>
        event EventHandler<StoreWriteFailedEventArgs>? WriteFailed;

        /// <summary>建表并确保连接可用；失败时置 <see cref="IsPersistent"/> 为 false，不抛异常。</summary>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>逐条写入检测记录。返回 false 表示本次仅落内存未入库。</summary>
        Task<bool> AppendDetectionRecordAsync(DetectionRecord record, CancellationToken cancellationToken = default);

        /// <summary>逐条写入告警记录。返回 false 表示本次仅落内存未入库。</summary>
        Task<bool> AppendAlertRecordAsync(AlertRecord record, CancellationToken cancellationToken = default);

        /// <summary>
        /// 加载 <paramref name="since"/>（含）之后的检测记录，按时间升序。
        /// <paramref name="maxCount"/> 限制返回条数（取最近的 N 条）。持久化不可用时返回空集合。
        /// </summary>
        Task<IReadOnlyList<DetectionRecord>> LoadDetectionRecordsAsync(
            DateTime since, int? maxCount = null, CancellationToken cancellationToken = default);

        /// <summary>加载 <paramref name="since"/>（含）之后的告警记录，按时间升序。持久化不可用时返回空集合。</summary>
        Task<IReadOnlyList<AlertRecord>> LoadAlertRecordsAsync(
            DateTime since, CancellationToken cancellationToken = default);

        /// <summary>读取最近一条检测记录的连续失败计数（冷启动延续），无记录时返回 null。</summary>
        Task<int?> LoadLatestConsecutiveFailuresAsync(CancellationToken cancellationToken = default);

        /// <summary>删除早于 <paramref name="before"/> 的检测记录，返回删除条数。</summary>
        Task<int> DeleteDetectionRecordsBeforeAsync(DateTime before, CancellationToken cancellationToken = default);

        /// <summary>删除早于 <paramref name="before"/> 的告警记录，返回删除条数。</summary>
        Task<int> DeleteAlertRecordsBeforeAsync(DateTime before, CancellationToken cancellationToken = default);

        /// <summary>清空全部检测记录（日志页“清空”，DESIGN 第六节 4），返回删除条数。</summary>
        Task<int> ClearDetectionRecordsAsync(CancellationToken cancellationToken = default);

        /// <summary>清空全部告警记录（告警历史页“清空”），返回删除条数。</summary>
        Task<int> ClearAlertRecordsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 切换数据目录前收尾：将 WAL 落盘并释放连接池，使数据库文件可被安全复制。
        /// 迁移完成后需重新 <see cref="InitializeAsync"/>。
        /// </summary>
        Task SealAsync(CancellationToken cancellationToken = default);
    }

    /// <summary><see cref="IStoreService.WriteFailed"/> 事件参数。</summary>
    public sealed class StoreWriteFailedEventArgs : EventArgs
    {
        public StoreWriteFailedEventArgs(string operation, string reason)
        {
            Operation = operation;
            Reason = reason;
        }

        /// <summary>失败的存储过程名称（如 append-detect）。</summary>
        public string Operation { get; }

        /// <summary>失败原因摘要，供界面提示与日志使用。</summary>
        public string Reason { get; }
    }
}
