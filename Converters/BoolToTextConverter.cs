using System.Globalization;
using System.Windows.Data;

namespace NetworkMonitorTool.Converters
{
    /// <summary>
    /// 布尔 → 文案。<paramref name="parameter"/> 为「true文案;false文案」，
    /// 缺省为「成功;失败」（DESIGN 第七节端口 / Ping 结果在表格中的展示）。
    /// </summary>
    public sealed class BoolToTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var trueText = "成功";
            var falseText = "失败";

            if (parameter is string { Length: > 0 } text)
            {
                var separator = text.IndexOf(';');
                if (separator >= 0)
                {
                    trueText = text[..separator];
                    falseText = text[(separator + 1)..];
                }
                else
                {
                    trueText = text;
                    falseText = string.Empty;
                }
            }

            return value is true ? trueText : falseText;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
