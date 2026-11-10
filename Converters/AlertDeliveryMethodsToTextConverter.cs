using System.Globalization;
using System.Windows.Data;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Converters
{
    /// <summary>
    /// 告警送达通道位标志 → 中文文案（DESIGN 第四节 3「告警方式」字段；界面与导出共用）。
    /// </summary>
    public sealed class AlertDeliveryMethodsToTextConverter : IValueConverter
    {
        public static string GetText(AlertDeliveryMethods methods)
        {
            var popup = methods.HasFlag(AlertDeliveryMethods.Popup);
            var tray = methods.HasFlag(AlertDeliveryMethods.TrayNotify);
            return (popup, tray) switch
            {
                (true, true) => "弹窗 + 托盘通知",
                (true, false) => "弹窗",
                (false, true) => "托盘通知",
                _ => "无",
            };
        }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is AlertDeliveryMethods methods ? GetText(methods) : string.Empty;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}