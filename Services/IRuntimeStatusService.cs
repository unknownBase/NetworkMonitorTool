using System.ComponentModel;
using System.Windows.Media;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 全局界面状态中心（DESIGN 第二节 2「全局状态」、第十节状态标识、
    /// DESIGN_S16_MULTITARGET 第八节多目标展示）。
    /// 汇总调度状态、各目标最新结果、连续失败次数与持久化降级提示，
    /// 供主窗口顶 / 底栏、仪表盘目标列表、工具条与托盘复用，保证各处含义一致。
    /// 事件源自调度后台线程，实现内部统一编组到界面线程后再对外通知。
    /// </summary>
    public interface IRuntimeStatusService : INotifyPropertyChanged
    {
        /// <summary>调度状态（未运行 / 运行中 / 暂停中）。</summary>
        SchedulerState State { get; }

        /// <summary>
        /// 各目标的运行期状态（与配置目标列表同序，界面线程更新）。
        /// 仪表盘目标列表、工具条目标选择与筛选下拉均复用该集合。
        /// </summary>
        IReadOnlyList<TargetStatus> Targets { get; }

        /// <summary>
        /// 汇总状态：暂停中优先，其次任一目标告警中，否则取各目标最近状态的「最差」
        /// （异常 &gt; Ping 失败 &gt; 端口失败 &gt; 成功）。
        /// </summary>
        DetectionStatus Status { get; }

        /// <summary>状态文案，含未运行 / 未检测。</summary>
        string StatusText { get; }

        /// <summary>状态取色，与 <see cref="StatusText"/> 一致。</summary>
        Brush StatusBrush { get; }

        /// <summary>最近一次检测时间文案（全部目标中最新的记录），供仪表盘与底栏复用。</summary>
        string LastDetectionText { get; }

        /// <summary>最近一次延迟文案（全部目标中最新的记录）。</summary>
        string LatencyText { get; }

        /// <summary>连续失败次数文案（汇总取各目标最大值）。</summary>
        string ConsecutiveFailuresText { get; }

        /// <summary>是否在任一目标上处于「已告警」状态（S7 接入，恢复成功后重置）。</summary>
        bool IsAlerting { get; }

        /// <summary>
        /// 工具条选定的目标 Id；null 表示汇总（DESIGN_S16_MULTITARGET 第八节 4）。
        /// 运行期状态，不写配置文件。
        /// </summary>
        string? SelectedTargetId { get; set; }

        /// <summary>选定的目标状态；汇总时为 null。</summary>
        TargetStatus? SelectedTarget { get; }

        /// <summary>工具条展示的状态文案（未选定目标时为汇总）。</summary>
        string SelectedStatusText { get; }

        /// <summary>工具条展示的状态取色（未选定目标时为汇总）。</summary>
        Brush SelectedStatusBrush { get; }

        /// <summary>工具条展示的延迟文案。</summary>
        string SelectedLatencyText { get; }

        /// <summary>工具条展示的连续失败次数文案。</summary>
        string SelectedConsecutiveFailuresText { get; }

        /// <summary>工具条展示的告警标记是否点亮。</summary>
        bool SelectedIsAlerting { get; }

        /// <summary>持久化是否可用；false 表示已降级为仅内存（DESIGN 第十四节 3）。</summary>
        bool IsPersistent { get; }

        /// <summary>降级提示文案，正常时为空。</summary>
        string? PersistenceHint { get; }

        /// <summary>新记录产生（已在界面线程），供仪表盘简表与日志页追加。</summary>
        event EventHandler<DetectionRecord>? RecordReceived;

        /// <summary>启动时回读各目标最近一条记录，使顶 / 底栏在首个检测完成前也有内容可显示。</summary>
        Task InitializeAsync(DateTime since, CancellationToken cancellationToken = default);
    }
}
