using System.Windows.Threading;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 告警送达编排（DESIGN 第八节 5）：订阅告警引擎事件，经界面线程弹窗与托盘气泡通知。
    /// 两个通道的开关每次触发时读取当前配置（保存后立即生效）。
    /// </summary>
    public sealed class AlertNotificationService
    {
        private readonly IConfigService _config;
        private readonly IDialogService _dialog;
        private readonly TrayIconService _tray;
        private readonly Dispatcher _dispatcher;

        public AlertNotificationService(
            IAlertService alerts,
            IConfigService config,
            IDialogService dialog,
            TrayIconService tray,
            Dispatcher dispatcher)
        {
            _config = config;
            _dialog = dialog;
            _tray = tray;
            _dispatcher = dispatcher;
            alerts.AlertTriggered += OnAlertTriggered;
        }

        private void OnAlertTriggered(object? sender, AlertRecord record)
        {
            if (_dispatcher.CheckAccess())
            {
                Deliver(record);
            }
            else
            {
                _dispatcher.BeginInvoke(() => Deliver(record));
            }
        }

        private void Deliver(AlertRecord record)
        {
            var alert = _config.Current.Alert;
            var target = record.Port is { } port ? $"{record.TargetIp}:{port}" : record.TargetIp;
            var failureAt = record.LastFailureAt ?? record.Timestamp;

            if (alert.PopupEnabled)
            {
                _dialog.ShowError(
                    $"目标 {target} 连续 {record.ConsecutiveFailures} 次检测失败，已触发告警。\n\n"
                    + $"最近失败时间：{failureAt:yyyy-MM-dd HH:mm:ss}\n"
                    + $"原因摘要：{record.ErrorSummary ?? "无"}\n\n"
                    + "恢复成功一次后告警状态与计数将自动重置。",
                    "连通性告警");
            }

            if (alert.TrayNotifyEnabled)
            {
                _tray.ShowAlert(
                    "连通性告警",
                    $"目标 {target} 连续 {record.ConsecutiveFailures} 次检测失败（最近失败 {failureAt:yyyy-MM-dd HH:mm:ss}）。");
            }
        }
    }
}