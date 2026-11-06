using System.IO;
using System.Text.Json;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 应用数据目录统一路径。
    /// 默认 %LOCALAPPDATA%\NetworkMonitorTool，可通过配置项 storage.dataDirectory 更改（S3 落地设置页与迁移）；
    /// 切换数据目录时需将数据库、日志等已有数据迁移至新目录（见 DESIGN 第十一节）。
    /// appsettings.json 始终保留在默认根目录，作为路径发现的锚点。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>默认数据根目录（同时也是配置文件的固定位置）</summary>
        public static string DefaultRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkMonitorTool");

        private static string? _root;

        /// <summary>
        /// 当前数据根目录。未显式设置时从 appsettings.json 读取 storage.dataDirectory，
        /// 缺失或非法（空/未盘符就绪）则回退 <see cref="DefaultRoot"/>。
        /// </summary>
        public static string Root
        {
            get => _root ??= ReadConfiguredRoot() ?? DefaultRoot;
            set => _root = value;
        }

        /// <summary>配置文件固定路径（始终位于默认根目录，不随数据目录迁移）</summary>
        public static string ConfigFile => Path.Combine(DefaultRoot, "appsettings.json");

        /// <summary>SQLite 数据库路径（S3 接入建表与持久化）</summary>
        public static string DatabaseFile => Path.Combine(Root, "monitor.db");

        /// <summary>日志目录（错误日志与后续运行日志）</summary>
        public static string LogDirectory => Path.Combine(Root, "logs");

        /// <summary>错误日志文件路径（全局未捕获异常写入处，关于页可查）</summary>
        public static string ErrorLogFile => Path.Combine(LogDirectory, "error.log");

        /// <summary>确保数据根目录存在</summary>
        public static void EnsureRoot() => Directory.CreateDirectory(Root);

        /// <summary>确保日志目录存在，返回日志目录路径</summary>
        public static string EnsureLogDirectory()
        {
            Directory.CreateDirectory(LogDirectory);
            return LogDirectory;
        }

        /// <summary>
        /// 轻量读取已配置的数据目录（仅解析 storage.dataDirectory 字段）。
        /// S3 接入完整 IConfigService 后由其取代此处逻辑。
        /// </summary>
        private static string? ReadConfiguredRoot()
        {
            try
            {
                if (!File.Exists(ConfigFile))
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigFile));
                if (doc.RootElement.TryGetProperty("storage", out var storage)
                    && storage.TryGetProperty("dataDirectory", out var dir))
                {
                    var value = dir.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
                // 配置缺失或损坏时取默认值（DESIGN 第五节：缺失字段取默认）
            }

            return null;
        }
    }
}
