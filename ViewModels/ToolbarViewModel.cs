using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 桌面工具条视图模型（DESIGN 第九节 2）：目标 IP、状态色块 / 文案、最近延迟、
    /// 连续失败次数与启动 / 停止 / 暂停 / 恢复 / 恢复主窗口操作，右键菜单另含退出。
    /// 状态绑定全局状态中心，与主窗口四处含义一致（含「告警中」红色标记）。
    /// </summary>
    public sealed partial class ToolbarViewModel : ObservableObject
    {
        private readonly IConfigService _config;
        private readonly ISchedulerService _scheduler;
        private readonly IWindowModeService _modes;

        public ToolbarViewModel(
            IRuntimeStatusService status,
            IConfigService config,
            ISchedulerService scheduler,
            IWindowModeService modes)
        {
            Status = status;
            _config = config;
            _scheduler = scheduler;
            _modes = modes;

            _state = status.State;
            status.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IRuntimeStatusService.State))
                {
                    State = status.State;
                }
            };
            config.ConfigChanged += (_, _) => RefreshTarget();

            RefreshTarget();
        }

        /// <summary>全局状态（色块、文案、延迟、连续失败与告警中标记共用）。</summary>
        public IRuntimeStatusService Status { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        private SchedulerState _state;

        [ObservableProperty] private string _targetText = string.Empty;

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

        /// <summary>恢复主窗口并回到上次停留页（DESIGN 第九节 4，双击同此路径）。</summary>
        [RelayCommand]
        private Task RestoreMainWindowAsync() => _modes.ShowMainWindowAsync();

        /// <summary>退出程序（DESIGN 第三节 3 工具条右键入口，经形态服务保存坐标后 Shutdown）。</summary>
        [RelayCommand]
        private Task ExitAsync() => _modes.ExitAsync();

        private void RefreshTarget()
        {
            var detection = _config.Current.Detection;
            TargetText = detection.Port is { } port
                ? $"{detection.TargetIp}:{port}"
                : detection.TargetIp;
        }
    }
}