using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 记录导出（DESIGN 第十二节）：UTF-8 带 BOM、逗号分隔、含字段转义。
    /// 失败时抛出异常，由调用方（界面命令）提示原因（DESIGN 第十四节 4）。
    /// </summary>
    public interface IExportService
    {
        /// <summary>将检测记录写入 CSV 文件，返回导出条数。</summary>
        Task<int> ExportDetectionRecordsAsync(
            IReadOnlyList<DetectionRecord> records, string filePath, CancellationToken cancellationToken = default);

        /// <summary>将告警记录写入 CSV 文件，返回导出条数（DESIGN 第四节 3 导出为可选增强，随 S7 交付）。</summary>
        Task<int> ExportAlertRecordsAsync(
            IReadOnlyList<AlertRecord> records, string filePath, CancellationToken cancellationToken = default);

        /// <summary>默认文件名：含目标 IP 与时间戳（DESIGN 第十二节 2），非法文件名字符替换为下划线。</summary>
        string BuildDefaultFileName(string targetIp);

        /// <summary>告警导出的默认文件名：告警历史_{目标}_{时间戳}.csv。</summary>
        string BuildAlertDefaultFileName(string targetIp);
    }
}