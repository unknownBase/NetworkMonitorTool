using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 仪表盘页视图模型（DESIGN 第四节 1）：状态大标识、目标与检测参数摘要、
    /// 最近检测时间 / 延迟 / 连续失败次数、快捷操作、最近记录简表与跳转日志页。
    /// 不放置详细配置与导出操作（分别在设置页、日志页）。
    /// </summary>
    public sealed partial class DashboardViewModel : PageViewModelBase
    {
        /// <summary>简表预览条数（完整历史在日志页查看）。</summary>
        private const int PreviewRecordCount = 8;

        private readonly IConfigService _config;
        private readonly ISchedulerService _scheduler;
        private readonly IStoreService _store;

        public DashboardViewModel(
            IRuntimeStatusService status,
            IConfigService config,
            ISchedulerService scheduler,
            IStoreService store)
        {
            Status = status;
            _config = config;
            _scheduler = scheduler;
            _store = store;

            _state = status.State;
            status.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IRuntimeStatusService.State))
                {
                    State = status.State;
                }
            };
            status.RecordReceived += (_, record) => OnRecordReceived(record);
            config.ConfigChanged += (_, _) => RefreshSummary();

            RefreshSummary();
        }

        public override string Key => "dashboard";

        public override string Title => "仪表盘";

        /// <summary>全局状态（XAML 直接绑定其文案与取色，保证四处含义一致）。</summary>
        public IRuntimeStatusService Status { get; }

        /// <summary>最近记录简表（时间倒序，最多 <see cref="PreviewRecordCount"/> 条）。</summary>
        public ObservableCollection<DetectionRecord> RecentRecords { get; } = new();

        /// <summary>跳转日志页查看全部记录（DESIGN 第三节 2，由壳层处理导航）。</summary>
        public event EventHandler? NavigateToLogRequested;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        private SchedulerState _state;

        [ObservableProperty] private string _targetSummary = string.Empty;
        [ObservableProperty] private string _intervalSummary = string.Empty;
        [ObservableProperty] private string _timeoutSummary = string.Empty;
        [ObservableProperty] private string _policySummary = string.Empty;

        /// <summary>简表为空时的占位提示。</summary>
        public bool HasRecentRecords => RecentRecords.Count > 0;

        public override async Task ActivateAsync()
        {
            await ReloadRecentRecordsAsync();
        }

        private async Task ReloadRecentRecordsAsync()
        {
            var records = await _store.LoadDetectionRecordsAsync(
                _config.Current.RetentionStart, maxCount: PreviewRecordCount);

            RecentRecords.Clear();
            // 存储层按时间升序返回，简表展示最新在前
            for (var i = records.Count - 1; i >= 0; i--)
            {
                RecentRecords.Add(records[i]);
            }

            OnPropertyChanged(nameof(HasRecentRecords));
        }

        private void OnRecordReceived(DetectionRecord record)
        {
            RecentRecords.Insert(0, record);
            while (RecentRecords.Count > PreviewRecordCount)
            {
                RecentRecords.RemoveAt(RecentRecords.Count - 1);
            }

            OnPropertyChanged(nameof(HasRecentRecords));
        }

        /// <summary>启动调度：按当前生效配置立即执行一次并周期触发。</summary>
        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartAsync()
        {
            var detection = _config.Current.Detection;
            await _scheduler.StartAsync(detection.ToDetectionOptions(), detection.Interval);
        }

        private bool CanStart() => State == SchedulerState.Stopped;

        [RelayCommand(CanExecute = nameof(CanStop))]
        private async Task StopAsync() => await _scheduler.StopAsync();

        private bool CanStop() => State != SchedulerState.Stopped;

        [RelayCommand(CanExecute = nameof(CanPause))]
        private void Pause() => _scheduler.Pause();

        private bool CanPause() => State == SchedulerState.Running;

        [RelayCommand(CanExecute = nameof(CanResume))]
        private void Resume() => _scheduler.Resume();

        private bool CanResume() => State == SchedulerState.Paused;

        /// <summary>
        /// 转为工具条：形态切换服务在 S8 交付，届时接 <c>IWindowModeService</c>。
        /// 本迭代先占位，按钮不可用，避免出现无响应的交互。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSwitchToToolbar))]
        private void SwitchToToolbar()
        {
        }

        private static bool CanSwitchToToolbar() => false;

        [RelayCommand]
        private void NavigateToLog() => NavigateToLogRequested?.Invoke(this, EventArgs.Empty);

        private void RefreshSummary()
        {
            var detection = _config.Current.Detection;

            TargetSummary = detection.Port is { } port
                ? $"{detection.TargetIp}:{port}"
                : detection.TargetIp;
            IntervalSummary = detection.Interval.ToString();
            TimeoutSummary = $"{detection.TimeoutMs} ms";
            PolicySummary = detection.PortPolicy switch
            {
                PortDetectionPolicy.Always => "始终检测",
                PortDetectionPolicy.OnlyWhenPingSuccess => "仅 Ping 成功时检测",
                PortDetectionPolicy.OnlyWhenPingFailed => "仅 Ping 失败时检测",
                _ => "不检测",
            };
        }
    }
}
