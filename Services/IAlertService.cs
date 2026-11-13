using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 连续失败告警引擎（DESIGN 第八节、DESIGN_S16_MULTITARGET 第七节）：
    /// 计数、阈值触发去重、冷启动延续与告警记录写入；S16 起计数与去重状态按目标独立维护。
    /// 弹窗 / 托盘通知等送达行为由 S7 订阅 <see cref="AlertTriggered"/> 实现。
    /// </summary>
    public interface IAlertService
    {
        /// <summary>当前连续失败次数（取各目标最大值，供汇总展示；成功清零，停止 / 暂停期间保留）。</summary>
        int ConsecutiveFailures { get; }

        /// <summary>是否在任一目标上处于「已告警」去重状态。</summary>
        bool IsAlerting { get; }

        /// <summary>指定目标的连续失败次数（目标从未判定时为 0）。</summary>
        int GetConsecutiveFailures(string targetId);

        /// <summary>指定目标是否处于「已告警」去重状态（恢复成功一次后重置）。</summary>
        bool IsAlertingFor(string targetId);

        /// <summary>阈值触发时产生（每个目标的每次连续失败越阈仅一次），参数为已写入历史的告警记录。</summary>
        event EventHandler<AlertRecord>? AlertTriggered;

        /// <summary>
        /// 冷启动延续：按目标分别读取历史中最近连续失败次数与最近失败摘要（DESIGN 第八节 3）。
        /// 应用启动、存储初始化后调用。
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 调度启动时调用：各目标计数已达阈值且未告警则按规则立即触发（DESIGN 第六节 1、第八节 3）。
        /// </summary>
        Task OnMonitoringStartedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 判定单条检测记录并返回填写连续失败计数后的副本（计数按记录的目标分账）；
        /// 达到阈值时写入告警历史并触发 <see cref="AlertTriggered"/>。
        /// 暂停期间调度不产生记录，故不计数、不告警（DESIGN 第八节 2）。
        /// </summary>
        Task<DetectionRecord> EvaluateAsync(DetectionRecord record, CancellationToken cancellationToken = default);
    }
}