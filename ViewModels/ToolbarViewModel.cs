using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 桌面工具条视图模型（DESIGN 第九节 2、DESIGN_S16_MULTITARGET 第八节 4）：
    /// 目标选择（汇总 / 单目标）、状态色块 / 文案、最近延迟、连续失败次数
    /// 与启动 / 停止 / 暂停 / 恢复 / 恢复主窗口操作，右键菜单另含退出。
    /// 状态绑定全局状态中心，与主窗口各处含义一致（含「告警中」红色标记）。
    /// 目标选择为运行期状态，重启后回到汇总。
    /// </summary>
    public sealed partial class ToolbarViewModel : ObservableObject
    {
        private readonly IConfigService _config;
        private readonly ISchedulerService _scheduler;
        private readonly IWindowModeService _modes;

        /// <summary>抑制选项重建时的选择回写。</summary>
        private bool _rebuildingOptions;

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
            config.ConfigChanged += (_, _) => RebuildTargetOptions();

            RebuildTargetOptions();
        }

        /// <summary>全局状态（色块、文案、延迟、连续失败与告警中标记共用）。</summary>
        public IRuntimeStatusService Status { get; }

        /// <summary>目标下拉选项：首项为汇总，其后为各启用目标。</summary>
        public ObservableCollection<ToolbarTargetOption> TargetOptions { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        private SchedulerState _state;

        [ObservableProperty] private ToolbarTargetOption? _selectedTargetOption;

        partial void OnSelectedTargetOptionChanged(ToolbarTargetOption? value)
        {
            if (!_rebuildingOptions)
            {
                Status.SelectedTargetId = value?.TargetId;
            }
        }

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

        /// <summary>恢复主窗口并回到上次停留页（DESIGN 第九节 4，双击同此路径）。</summary>
        [RelayCommand]
        private Task RestoreMainWindowAsync() => _modes.ShowMainWindowAsync();

        /// <summary>退出程序（DESIGN 第九节 3 工具条右键入口，经形态服务保存坐标后 Shutdown）。</summary>
        [RelayCommand]
        private Task ExitAsync() => _modes.ExitAsync();

        /// <summary>重建目标下拉：首项汇总 + 各启用目标；尽量保留当前选择。</summary>
        private void RebuildTargetOptions()
        {
            var previousId = SelectedTargetOption?.TargetId;
            _rebuildingOptions = true;
            try
            {
                TargetOptions.Clear();
                TargetOptions.Add(new ToolbarTargetOption(null, "汇总（全部目标）"));
                foreach (var target in _config.Current.Targets.Where(t => t.Enabled))
                {
                    TargetOptions.Add(new ToolbarTargetOption(target.Id, target.DisplayName));
                }

                SelectedTargetOption = TargetOptions.FirstOrDefault(o => o.TargetId == previousId)
                    ?? TargetOptions[0];
                Status.SelectedTargetId = SelectedTargetOption.TargetId;
                StartCommand.NotifyCanExecuteChanged();
            }
            finally
            {
                _rebuildingOptions = false;
            }
        }

        /// <summary>目标下拉选项：TargetId 为 null 表示汇总。</summary>
        public sealed record ToolbarTargetOption(string? TargetId, string Label);
    }
}
