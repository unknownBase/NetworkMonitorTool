using System.IO;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 应用数据目录统一路径。
    /// 默认 %LOCALAPPDATA%\NetworkMonitorTool，实际取值由 <see cref="IConfigService"/> 在启动时读取
    /// appsettings.json 的 storage.dataDirectory 后写入 <see cref="Root"/>（S3 落地，含合法性校验）；
    /// 切换数据目录时需将数据库、日志等已有数据迁移至新目录（见 DESIGN 第十一节、<see cref="IDataMigrationService"/>）。
    /// appsettings.json 始终保留在 <see cref="DefaultRoot"/>，作为路径发现的锚点。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>默认数据根目录（同时也是配置文件的固定位置）</summary>
        public static string DefaultRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkMonitorTool");

        private static string? _root;

        /// <summary>
        /// 当前数据根目录。未经配置服务写入时取 <see cref="DefaultRoot"/>；
        /// 赋值即生效（配置加载 / 目录迁移成功后由相应服务设置）。
        /// </summary>
        public static string Root
        {
            get => _root ??= DefaultRoot;
            set => _root = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>配置文件固定路径（始终位于默认根目录，不随数据目录迁移）</summary>
        public static string ConfigFile => Path.Combine(DefaultRoot, "appsettings.json");

        /// <summary>SQLite 数据库路径</summary>
        public static string DatabaseFile => Path.Combine(Root, "monitor.db");

        /// <summary>日志目录（错误日志与运行日志）</summary>
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
    }
}
