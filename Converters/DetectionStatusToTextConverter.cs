using System.Globalization;
using System.Windows.Data;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Converters
{
    /// <summary>
    /// 检测状态 → 中文文案（与 <see cref="DetectionStatusToBrushConverter"/> 的颜色一一对应，
    /// DESIGN 第十节要求仪表盘、日志、工具条、托盘四处含义一致）。
    /// </summary>
    public sealed class DetectionStatusToTextConverter : IValueConverter
    {
        public static string GetText(DetectionStatus status) => status switch
        {
            DetectionStatus.Success => "成功",
            DetectionStatus.PingFailed => "Ping 失败",
            DetectionStatus.PortFailed => "端口失败",
            DetectionStatus.Error => "异常",
            DetectionStatus.Paused => "暂停中",
            DetectionStatus.Alerting => "告警中",
            _ => status.ToString(),
        };

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is DetectionStatus status ? GetText(status) : string.Empty;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
