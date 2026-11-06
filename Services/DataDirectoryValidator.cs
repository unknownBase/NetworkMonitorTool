using System.IO;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 数据目录合法性校验（DESIGN 第十一节 1 + DEVELOPMENT_PLAN S3）：
    /// 绝对路径、盘符就绪、可创建、可写、磁盘剩余空间充足。
    /// 空值表示使用默认目录，直接视为合法。
    /// </summary>
    internal static class DataDirectoryValidator
    {
        /// <summary>切换目录时要求的最低剩余空间（除已有数据体积外的余量）。</summary>
        internal const long MinimumFreeBytes = 64L * 1024 * 1024;

        /// <summary>
        /// 校验目录可用性。
        /// </summary>
        /// <param name="directory">待校验目录；null / 空白表示使用默认目录（合法）。</param>
        /// <param name="error">不合法时的中文提示；合法时为 null。</param>
        /// <param name="requiredBytes">期望的剩余空间下限（如待迁移数据体积），0 表示仅按 <see cref="MinimumFreeBytes"/>。</param>
        internal static bool TryValidate(string? directory, out string? error, long requiredBytes = 0)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(directory))
            {
                return true; // 未配置即默认目录
            }

            try
            {
                if (!Path.IsPathRooted(directory))
                {
                    error = $"数据目录必须为绝对路径：{directory}";
                    return false;
                }

                var full = Path.GetFullPath(directory);
                if (full.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    error = $"数据目录包含非法字符：{full}";
                    return false;
                }

                var root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root))
                {
                    error = $"无法识别数据目录所在磁盘：{full}";
                    return false;
                }

                // UNC 路径无 DriveInfo，仅能做写入探测
                if (!root.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    var drive = new DriveInfo(root);
                    if (!drive.IsReady)
                    {
                        error = $"数据目录所在磁盘未就绪：{drive.Name}";
                        return false;
                    }

                    var required = Math.Max(MinimumFreeBytes, requiredBytes + MinimumFreeBytes / 4);
                    if (drive.AvailableFreeSpace < required)
                    {
                        error = $"数据目录所在磁盘剩余空间不足（需约 {required / 1024 / 1024} MB，"
                              + $"实际 {drive.AvailableFreeSpace / 1024 / 1024} MB）";
                        return false;
                    }
                }

                // 可创建 + 可写探测
                Directory.CreateDirectory(full);
                var probe = Path.Combine(full, $".nmt-write-probe-{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllText(probe, string.Empty);
                }
                finally
                {
                    TryDelete(probe);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"数据目录不可用：{ex.Message}";
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 探测文件删除失败不影响校验结论
            }
        }
    }
}
