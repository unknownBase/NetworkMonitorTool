using System.Windows.Threading;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 告警送达编排（DESIGN 第八节 5）：订阅告警引擎事件，经界面线程弹窗。
    /// 弹窗开关每次触发读取当前配置（保存后立即生效）；
    /// 托盘通知通道随 S9 托盘图标接入，开关与送达记录已先行生效。
    /// </summary>
    public sealed class AlertNotificationService
    {
        private readonly IConfigService _config;
        private readonly IDialogService _dialog;
        private readonly Dispatcher _dispatcher;

        public AlertNotificationService(
            IAlertService alerts,
            IConfigService config,
            IDialogService dialog,
            Dispatcher dispatcher)
        {
            _config = config;
            _dialog = dialog;
            _dispatcher = dispatcher;
            alerts.AlertTriggered += OnAlertTriggered;
        }

        private void OnAlertTriggered(object? sender, AlertRecord record)
        {
            if (_dispatcher.CheckAccess())
            {
                ShowPopup(record);
            }
            else
            {
                _dispatcher.BeginInvoke(() => ShowPopup(record));
            }
        }

        private void ShowPopup(AlertRecord record)
        {
            if (!_config.Current.Alert.PopupEnabled)
            {
                return;
            }

            var target = record.Port is { } port ? $"{record.TargetIp}:{port}" : record.TargetIp;
            var failureAt = record.LastFailureAt ?? record.Timestamp;
            _dialog.ShowError(
                $"目标 {target} 连续 {record.ConsecutiveFailures} 次检测失败，已触发告警。\n\n"
                + $"最近失败时间：{failureAt:yyyy-MM-dd HH:mm:ss}\n"
                + $"原因摘要：{record.ErrorSummary ?? "无"}\n\n"
                + "恢复成功一次后告警状态与计数将自动重置。",
                "连通性告警");
        }
    }
}