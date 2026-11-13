using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 周期调度服务：按间隔触发检测，支持启动 / 停止 / 暂停 / 恢复，与页面完全解耦
    /// （DESIGN 第二、六节）。S16 起每个周期并行检测全部启用目标，检测通过事件向外推送，
    /// 由订阅方（ViewModel / 存储 / 告警）处理。
    /// </summary>
    public interface ISchedulerService : IDisposable
    {
        /// <summary>当前调度状态。</summary>
        SchedulerState State { get; }

        /// <summary>每个目标每完成一次检测触发（在后台线程）。</summary>
        event EventHandler<DetectionRecord>? DetectionCompleted;

        /// <summary>状态变化触发（在后台线程）。</summary>
        event EventHandler<SchedulerState>? StateChanged;

        /// <summary>
        /// 启动调度：立即对各目标并行执行一次并按间隔周期触发。重复调用无效果。
        /// <paramref name="targets"/> 为启用目标快照（至少一个，空集合抛异常）。
        /// </summary>
        Task StartAsync(
            IReadOnlyList<DetectionOptions> targets, DetectionInterval interval,
            CancellationToken cancellationToken = default);

        /// <summary>暂停：停止计时与检测，保留状态。运行中调用有效。</summary>
        void Pause();

        /// <summary>恢复：按原间隔继续。暂停中调用有效。</summary>
        void Resume();

        /// <summary>停止：结束调度，回到未运行状态，等待当前周期退出。</summary>
        Task StopAsync();
    }
}
