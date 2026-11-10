using System.ComponentModel;
using System.Windows.Media;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 全局界面状态中心（DESIGN 第二节 2「全局状态」、第十节状态标识）。
    /// 汇总调度状态、最近一次检测结果、连续失败次数与持久化降级提示，
    /// 供主窗口顶 / 底栏、仪表盘以及后续的工具条（S8）、托盘（S9）复用，保证四处含义一致。
    /// 事件源自调度后台线程，实现内部统一编组到界面线程后再对外通知。
    /// </summary>
    public interface IRuntimeStatusService : INotifyPropertyChanged
    {
        /// <summary>调度状态（未运行 / 运行中 / 暂停中）。</summary>
        SchedulerState State { get; }

        /// <summary>界面状态：暂停中优先于告警中，告警中优先于最近检测结果（DESIGN 第十节）。</summary>
        DetectionStatus Status { get; }

        /// <summary>状态文案，含未运行。</summary>
        string StatusText { get; }

        /// <summary>状态取色，与 <see cref="StatusText"/> 一致。</summary>
        Brush StatusBrush { get; }

        /// <summary>最近一次检测时间文案（无记录时为占位文案），供仪表盘与底栏复用。</summary>
        string LastDetectionText { get; }

        /// <summary>最近一次延迟文案。</summary>
        string LatencyText { get; }

        /// <summary>连续失败次数文案。</summary>
        string ConsecutiveFailuresText { get; }

        /// <summary>最近一条检测记录，未检测过时为 null。</summary>
        DetectionRecord? LastRecord { get; }

        /// <summary>最近一次检测后的连续失败次数（S6 起由告警引擎填写）。</summary>
        int ConsecutiveFailures { get; }

        /// <summary>是否处于「已告警」状态（S7 接入，恢复成功后重置）。</summary>
        bool IsAlerting { get; }

        /// <summary>持久化是否可用；false 表示已降级为仅内存（DESIGN 第十四节 3）。</summary>
        bool IsPersistent { get; }

        /// <summary>降级提示文案，正常时为空。</summary>
        string? PersistenceHint { get; }

        /// <summary>新记录产生（已在界面线程），供仪表盘简表与日志页追加。</summary>
        event EventHandler<DetectionRecord>? RecordReceived;

        /// <summary>启动时回读最近一条记录，使顶 / 底栏在首个检测完成前也有内容可显示。</summary>
        Task InitializeAsync(DateTime since, CancellationToken cancellationToken = default);
    }
}
