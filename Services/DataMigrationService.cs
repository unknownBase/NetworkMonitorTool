using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 数据目录迁移实现（DESIGN 第十一节 1）。
    /// 流程：校验目标 → 收尾旧库（WAL 落盘、释放连接池）→ 复制数据库（含 -wal / -shm）与日志
    /// → 完整性与记录数验证 → 重写配置并切换目录 → 重新初始化存储。
    /// 任一步失败：清理本次复制出的文件、保持旧目录生效、重新加载磁盘配置，不丢任何数据
    /// （旧目录中的原数据不做删除，由用户自行处置）。
    /// </summary>
    public sealed class DataMigrationService : IDataMigrationService
    {
        private readonly IConfigService _config;
        private readonly IStoreService _store;
        private readonly ILogger<DataMigrationService>? _logger;

        public DataMigrationService(
            IConfigService config,
            IStoreService store,
            ILogger<DataMigrationService>? logger = null)
        {
            _config = config;
            _store = store;
            _logger = logger;
        }

        public long GetUsedBytes()
        {
            try
            {
                return SumDirectoryBytes(AppPaths.Root);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<MigrationResult> MigrateAsync(
            string? targetDirectory, CancellationToken cancellationToken = default)
        {
            var from = AppPaths.Root;
            var to = string.IsNullOrWhiteSpace(targetDirectory)
                ? AppPaths.DefaultRoot
                : Path.GetFullPath(targetDirectory.Trim());

            if (string.Equals(Normalize(from), Normalize(to), StringComparison.OrdinalIgnoreCase))
            {
                return MigrationResult.NotNeeded(to);
            }

            if (!DataDirectoryValidator.TryValidate(to, out var error, GetUsedBytes()))
            {
                return MigrationResult.Failed(from, to, error ?? "目标目录不可用");
            }

            var copied = new List<string>();

            try
            {
                // 1. 收尾旧库，使数据库文件自包含且不被占用
                await _store.SealAsync(cancellationToken).ConfigureAwait(false);

                // 2. 迁移前记录数基线（只读源库）
                var baseline = ReadRecordCounts(from);

                // 3. 复制数据库附属文件与日志
                foreach (var file in EnumerateDataFiles(from))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = ToTargetPath(from, to, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, overwrite: true);
                    copied.Add(destination);
                }

                // 4. 验证新库：完整性 + 记录数一致
                //    源目录尚无数据库（首次启动即改目录 / 已降级未落库）时无内容可验证，
                //    不能因此判定失败，否则目录永远切不过去。
                if (baseline.DatabaseExists && !VerifyDatabase(to, baseline, out var verifyMessage))
                {
                    return await FailAsync(from, to, copied, $"数据验证未通过：{verifyMessage}",
                        reloadConfig: false, cancellationToken).ConfigureAwait(false);
                }

                // 5. 重写配置（默认目录以 null 表示）并切换生效路径
                var config = _config.Current;
                config.Storage.DataDirectory = Normalize(to) == Normalize(AppPaths.DefaultRoot)
                    ? null
                    : to;

                if (!await _config.SaveAsync(config, cancellationToken).ConfigureAwait(false))
                {
                    return await FailAsync(from, to, copied, "配置写入失败，已保持原数据目录",
                        reloadConfig: true, cancellationToken).ConfigureAwait(false);
                }

                // SaveAsync 已按新配置设置 AppPaths.Root，此处仅重建存储连接
                await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);

                var message = $"数据目录已切换至 {to}（复制 {copied.Count} 个文件，原目录数据保留）";
                _logger?.LogInformation(message);
                return MigrationResult.Ok(from, to, copied.Count, message);
            }
            catch (OperationCanceledException)
            {
                return await FailAsync(from, to, copied, "迁移已取消，保持原数据目录",
                    reloadConfig: true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "数据目录迁移失败：{From} → {To}", from, to);
                return await FailAsync(from, to, copied, $"迁移失败：{ex.Message}",
                    reloadConfig: true, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>失败收口：清理复制出的文件、恢复旧目录并重新初始化存储。</summary>
        private async Task<MigrationResult> FailAsync(
            string from, string to, List<string> copied, string reason, bool reloadConfig,
            CancellationToken cancellationToken)
        {
            foreach (var file in copied)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 残留副本不影响旧目录可用性，忽略
                }
            }

            try
            {
                if (reloadConfig)
                {
                    // 以磁盘配置为准恢复内存态与生效路径
                    _config.Load();
                }

                AppPaths.Root = from;
                await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "迁移失败后回退旧目录存储初始化异常");
            }

            _logger?.LogWarning("数据目录迁移未完成，保持 {From}：{Reason}", from, reason);
            return MigrationResult.Failed(from, to, reason);
        }

        /// <summary>迁移基线：源目录是否存在数据库文件与两张表的记录数。</summary>
        private readonly record struct MigrationBaseline(bool DatabaseExists, long Detect, long Alert);

        /// <summary>打开目标库做完整性检查，并比对两张表的记录数。</summary>
        private bool VerifyDatabase(string targetRoot, MigrationBaseline expected, out string message)
        {
            var dbPath = Path.Combine(targetRoot, Path.GetFileName(AppPaths.DatabaseFile));
            if (!File.Exists(dbPath))
            {
                message = "新目录中未找到数据库文件";
                return false;
            }

            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadWrite,
                };

                using var conn = new SqliteConnection(builder.ToString());
                conn.Open();

                using (var check = conn.CreateCommand())
                {
                    check.CommandText = "PRAGMA integrity_check;";
                    var result = check.ExecuteScalar()?.ToString();
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        message = $"integrity_check 返回 {result}";
                        return false;
                    }
                }

                // 表可能尚未创建（全新目录无历史数据），此时期望值同为 0
                var actualDetect = CountRows(conn, "detect_record");
                var actualAlert = CountRows(conn, "alert_record");

                if (actualDetect < expected.Detect || actualAlert < expected.Alert)
                {
                    message = $"记录数不一致：检测 {actualDetect}/{expected.Detect}，告警 {actualAlert}/{expected.Alert}";
                    return false;
                }

                message = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private MigrationBaseline ReadRecordCounts(string sourceRoot)
        {
            var dbPath = Path.Combine(sourceRoot, Path.GetFileName(AppPaths.DatabaseFile));
            if (!File.Exists(dbPath))
            {
                return new MigrationBaseline(false, 0, 0);
            }

            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadOnly,
                };

                using var conn = new SqliteConnection(builder.ToString());
                conn.Open();
                return new MigrationBaseline(true,
                    CountRows(conn, "detect_record"), CountRows(conn, "alert_record"));
            }
            catch (Exception ex)
            {
                // 源库不可读时以“无库”为基线：新库仍会建表，后续回读可暴露真实问题
                _logger?.LogWarning(ex, "迁移前读取源数据库记录数失败");
                return new MigrationBaseline(false, 0, 0);
            }
        }

        private static long CountRows(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            // 表名来自本类常量，非用户输入
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = $name;";
            cmd.Parameters.AddWithValue("$name", table);
            if (Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) == 0)
            {
                return 0;
            }

            cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        }

        /// <summary>待迁移文件：根目录下的数据库及附属文件、窗口状态文件 + 日志目录全部内容。</summary>
        private static IEnumerable<string> EnumerateDataFiles(string root)
        {
            var dbFileName = Path.GetFileName(AppPaths.DatabaseFile);
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = Path.Combine(root, dbFileName + suffix);
                if (File.Exists(path))
                {
                    yield return path;
                }
            }

            var statePath = Path.Combine(root, Path.GetFileName(AppPaths.WindowStateFile));
            if (File.Exists(statePath))
            {
                yield return statePath;
            }

            if (Directory.Exists(AppPaths.LogDirectory) &&
                Normalize(AppPaths.LogDirectory).StartsWith(Normalize(root) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in Directory.EnumerateFiles(AppPaths.LogDirectory, "*", SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }
        }

        private static string ToTargetPath(string from, string to, string file)
        {
            var relative = Path.GetRelativePath(from, file);
            return Path.Combine(to, relative);
        }

        private static long SumDirectoryBytes(string root)
        {
            if (!Directory.Exists(root))
            {
                return 0;
            }

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // 单个文件不可读不影响总体估算
                }
            }

            return total;
        }

        private static string Normalize(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
