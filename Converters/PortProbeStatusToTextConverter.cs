using System.Globalization;
using System.Windows.Data;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Converters
{
    /// <summary>
    /// 端口结果三态 → 中文文案（DESIGN 第七节：界面与导出均需体现）。
    /// </summary>
    public sealed class PortProbeStatusToTextConverter : IValueConverter
    {
        public static string GetText(PortProbeStatus status) => status switch
        {
            PortProbeStatus.Success => "成功",
            PortProbeStatus.Failed => "失败",
            _ => "未检测",
        };

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is PortProbeStatus status ? GetText(status) : string.Empty;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
