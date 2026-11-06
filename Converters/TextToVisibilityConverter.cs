using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NetworkMonitorTool.Converters
{
    /// <summary>
    /// 文本 → 可见性：非空白时可见（设置页行内校验提示，DESIGN 第五节「行内提示不弹窗」）。
    /// </summary>
    public sealed class TextToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
