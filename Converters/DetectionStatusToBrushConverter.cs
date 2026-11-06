using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Converters
{
    /// <summary>
    /// 检测状态 → 颜色 转换器（DESIGN 第十节）。
    /// 成功=绿、Ping失败=红、端口失败=橙、异常=紫、暂停中=灰、告警中=红（醒目标记由上层叠加）。
    /// </summary>
    public sealed class DetectionStatusToBrushConverter : IValueConverter
    {
        private static readonly Brush SuccessBrush = Freeze(Colors.LimeGreen);
        private static readonly Brush PingFailedBrush = Freeze(Colors.Red);
        private static readonly Brush PortFailedBrush = Freeze(Colors.Orange);
        private static readonly Brush ErrorBrush = Freeze(Colors.Purple);
        private static readonly Brush PausedBrush = Freeze(Colors.Gray);
        private static readonly Brush AlertingBrush = Freeze(Colors.Crimson);
        private static readonly Brush FallbackBrush = Freeze(Colors.Transparent);

        /// <summary>供工具条 / 仪表盘等非绑定场景复用的状态取色。</summary>
        public static Brush GetBrush(DetectionStatus status) => status switch
        {
            DetectionStatus.Success => SuccessBrush,
            DetectionStatus.PingFailed => PingFailedBrush,
            DetectionStatus.PortFailed => PortFailedBrush,
            DetectionStatus.Error => ErrorBrush,
            DetectionStatus.Paused => PausedBrush,
            DetectionStatus.Alerting => AlertingBrush,
            _ => FallbackBrush,
        };

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is DetectionStatus status ? GetBrush(status) : FallbackBrush;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
