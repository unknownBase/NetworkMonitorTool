using System.IO;
using System.Text;
using NetworkMonitorTool.Converters;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 检测记录 CSV 导出（DESIGN 第十二节）：UTF-8 带 BOM，列与日志页表格一致并补充端口列；
    /// 状态文案复用界面转换器的静态取文案方法，保证「界面与导出均体现」同一含义。
    /// </summary>
    public sealed class CsvExportService : IExportService
    {
        private static readonly string[] Headers =
            { "时间", "目标IP", "端口", "Ping结果", "延迟(ms)", "端口结果", "整体状态", "错误摘要" };

        private static readonly char[] CharsRequiringQuotes = { ',', '"', '\n', '\r' };

        public async Task<int> ExportDetectionRecordsAsync(
            IReadOnlyList<DetectionRecord> records, string filePath, CancellationToken cancellationToken = default)
        {
            await using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(true));
            await writer.WriteLineAsync(string.Join(",", Headers));

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fields = new[]
                {
                    record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    record.TargetIp,
                    record.Port?.ToString() ?? string.Empty,
                    record.PingSuccess ? "成功" : "失败",
                    record.LatencyMs?.ToString() ?? string.Empty,
                    PortProbeStatusToTextConverter.GetText(record.PortResult),
                    DetectionStatusToTextConverter.GetText(record.Status),
                    record.ErrorSummary ?? string.Empty,
                };

                await writer.WriteLineAsync(string.Join(",", fields.Select(Escape)));
            }

            return records.Count;
        }

        public string BuildDefaultFileName(string targetIp)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safeIp = string.Concat(targetIp.Select(c => invalid.Contains(c) ? '_' : c));
            return $"检测日志_{safeIp}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        }

        /// <summary>RFC 4180 转义：含逗号 / 引号 / 换行的字段整体加引号，内部引号翻倍。</summary>
        private static string Escape(string field)
            => field.IndexOfAny(CharsRequiringQuotes) >= 0
                ? $"\"{field.Replace("\"", "\"\"")}\""
                : field;
    }
}