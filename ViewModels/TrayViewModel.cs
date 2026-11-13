using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 托盘视图模型（DESIGN 第三节 3.4、DESIGN_S16_MULTITARGET 第八节 5）：常驻图标的提示文案与右键菜单命令。
    /// 状态绑定全局状态中心，与主窗口 / 工具条含义一致（含「告警中」文案）；
    /// 菜单提供显示主窗口、转为工具条、启动 / 停止 / 暂停 / 恢复检测与退出，启停作用于全部启用目标。
    /// </summary>
    public sealed partial class TrayViewModel : ObservableObject
    {
        private readonly IConfigService _config;
        private readonly ISchedulerService _scheduler;
        private readonly IWindowModeService _modes;

        public TrayViewModel(
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
                if (e.PropertyName is nameof(IRuntimeStatusService.State)
                    or nameof(IRuntimeStatusService.StatusText)
                    or nameof(IRuntimeStatusService.ConsecutiveFailuresText))
                {
                    ToolTipText = BuildToolTip();
                }
            };
            config.ConfigChanged += (_, _) => ToolTipText = BuildToolTip();

            ToolTipText = BuildToolTip();
        }

        public IRuntimeStatusService Status { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        private SchedulerState _state;

        /// <summary>托盘悬浮提示：状态 · 目标数量 · 最高连续失败次数（随检测与配置变化刷新）。</summary>
        [ObservableProperty] private string _toolTipText = string.Empty;

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartAsync()
        {
            var config = _config.Current;
            await _scheduler.StartAsync(config.GetEnabledDetectionOptions(), config.Detection.Interval);
        }

        private bool CanStart() => State == SchedulerState.Stopped && _config.Current.HasEnabledTarget;

        [RelayCommand(CanExecute = nameof(CanStop))]
        private async Task StopAsync() => await _scheduler.StopAsync();

        private bool CanStop() => State != SchedulerState.Stopped;

        [RelayCommand(CanExecute = nameof(CanPause))]
        private void Pause() => _scheduler.Pause();

        private bool CanPause() => State == SchedulerState.Running;

        [RelayCommand(CanExecute = nameof(CanResume))]
        private void Resume() => _scheduler.Resume();

        private bool CanResume() => State == SchedulerState.Paused;

        /// <summary>显示主窗口并回到上次停留页（双击托盘图标同此路径）。</summary>
        [RelayCommand]
        private Task ShowMainWindowAsync() => _modes.ShowMainWindowAsync();

        /// <summary>转为桌面工具条。</summary>
        [RelayCommand]
        private Task ShowToolbarAsync() => _modes.ShowToolbarAsync();

        /// <summary>退出程序（托盘菜单入口，经形态服务保存坐标后统一 Shutdown）。</summary>
        [RelayCommand]
        private Task ExitAsync() => _modes.ExitAsync();

        private string BuildToolTip()
        {
            var config = _config.Current;
            var enabled = config.Targets.Count(t => t.Enabled);
            return $"网络连通性监测工具｜{Status.StatusText}｜目标 {enabled}/{config.Targets.Count} 个启用｜最高连续失败 {Status.ConsecutiveFailuresText}";
        }
    }
}
