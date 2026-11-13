using System.IO;
using NetworkMonitorTool.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// SQLite 持久化实现：detect_record / alert_record 分表（DESIGN 第十一节 1）。
    /// 所有库操作经同一异步门串行执行，规避 SQLite 单写锁竞争（见 DEVELOPMENT_PLAN 风险表）；
    /// 任何写库异常均降级为“仅内存”，通过 <see cref="WriteFailed"/> 通知界面提示，不向上抛出。
    /// 时间统一以本地时间 Ticks（INTEGER）存储，保证范围比较与排序可用。
    /// </summary>
    public sealed class SqliteStoreService : IStoreService
    {
        private const string DetectTableName = "detect_record";
        private const string AlertTableName = "alert_record";

        /// <summary>降级后重试恢复连接的间隔。</summary>
        private static readonly TimeSpan RecoveryRetryDelay = TimeSpan.FromSeconds(30);

        private readonly ILogger<SqliteStoreService>? _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private string? _dataSource;
        private volatile bool _persistent;
        private DateTime _nextRetryAt;

        public SqliteStoreService(ILogger<SqliteStoreService>? logger = null)
        {
            _logger = logger;
        }

        public bool IsPersistent => _persistent;

        public event EventHandler<StoreWriteFailedEventArgs>? WriteFailed;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>建表 + WAL。调用方须持有 <paramref name="_gate"/>；成功置 _persistent=true。</summary>
        private async Task<bool> InitializeCoreAsync(CancellationToken ct)
        {
            try
            {
                // 每次初始化都按当前数据根目录重解析，支持迁移后切换到新路径
                _dataSource = AppPaths.DatabaseFile;
                Directory.CreateDirectory(AppPaths.Root);

                await using var conn = await OpenAsync(ct).ConfigureAwait(false);
                await using (var pragma = conn.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA journal_mode=WAL;";
                    await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"""
                        CREATE TABLE IF NOT EXISTS {DetectTableName} (
                            id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                            timestamp            INTEGER NOT NULL,
                            target_id            TEXT    NOT NULL DEFAULT '',
                            target_ip            TEXT    NOT NULL,
                            port                 INTEGER NULL,
                            ping_success         INTEGER NOT NULL,
                            latency_ms           INTEGER NULL,
                            port_result          INTEGER NOT NULL,
                            status               INTEGER NOT NULL,
                            error_summary        TEXT    NULL,
                            consecutive_failures INTEGER NOT NULL DEFAULT 0
                        );
                        """;
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"""
                        CREATE TABLE IF NOT EXISTS {AlertTableName} (
                            id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                            timestamp            INTEGER NOT NULL,
                            target_id            TEXT    NOT NULL DEFAULT '',
                            target_ip            TEXT    NOT NULL,
                            port                 INTEGER NULL,
                            consecutive_failures INTEGER NOT NULL,
                            error_summary        TEXT    NULL,
                            delivery_methods     INTEGER NOT NULL
                        );
                        """;
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // S16：旧库补充目标列（幂等：PRAGMA 探测后再 ALTER）
                await EnsureColumnAsync(conn, DetectTableName, "target_id", "TEXT NOT NULL DEFAULT ''", ct)
                    .ConfigureAwait(false);
                await EnsureColumnAsync(conn, AlertTableName, "target_id", "TEXT NOT NULL DEFAULT ''", ct)
                    .ConfigureAwait(false);

                // S7：旧库补充最近失败时间列（新库同样经此路径补齐，PRAGMA 探测保证幂等）
                await EnsureColumnAsync(conn, AlertTableName, "last_failure_at", "INTEGER NULL", ct)
                    .ConfigureAwait(false);

                // S16：存量记录归属 v1 迁移目标（与 TargetConfig.LegacyTargetId 对齐，幂等）
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"""
                        UPDATE {DetectTableName} SET target_id = $legacy WHERE target_id = '';
                        UPDATE {AlertTableName} SET target_id = $legacy WHERE target_id = '';
                        """;
                    cmd.Parameters.AddWithValue("$legacy", TargetConfig.LegacyTargetId);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // 建索引须在补列之后：旧库才有 target_id 可索引
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"""
                        CREATE INDEX IF NOT EXISTS idx_{DetectTableName}_timestamp
                            ON {DetectTableName}(timestamp);
                        CREATE INDEX IF NOT EXISTS idx_{DetectTableName}_target_timestamp
                            ON {DetectTableName}(target_id, timestamp);
                        CREATE INDEX IF NOT EXISTS idx_{AlertTableName}_timestamp
                            ON {AlertTableName}(timestamp);
                        CREATE INDEX IF NOT EXISTS idx_{AlertTableName}_target_timestamp
                            ON {AlertTableName}(target_id, timestamp);
                        """;
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                SetPersistent(true);
                return true;
            }
            catch (Exception ex)
            {
                SetPersistent(false);
                _logger?.LogError(ex, "SQLite 初始化失败，持久化降级为仅内存");
                return false;
            }
        }

        /// <summary>列不存在时补充（幂等）；调用方须持有 <paramref name="_gate"/>。</summary>
        private static async Task EnsureColumnAsync(
            SqliteConnection conn, string table, string column, string definition, CancellationToken ct)
        {
            var hasColumn = false;
            await using (var probe = conn.CreateCommand())
            {
                probe.CommandText = $"PRAGMA table_info({table});";
                await using var reader = await probe.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    {
                        hasColumn = true;
                        break;
                    }
                }
            }

            if (hasColumn)
            {
                return;
            }

            await using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        public async Task<bool> AppendDetectionRecordAsync(
            DetectionRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
                {
                    Degrade("append-detect", "持久化不可用");
                    return false;
                }

                await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    INSERT INTO {DetectTableName}
                        (timestamp, target_id, target_ip, port, ping_success, latency_ms,
                         port_result, status, error_summary, consecutive_failures)
                    VALUES ($ts, $targetId, $ip, $port, $ping, $latency, $portResult, $status, $error, $consecutive);
                    """;
                cmd.Parameters.AddWithValue("$ts", record.Timestamp.Ticks);
                cmd.Parameters.AddWithValue("$targetId", record.TargetId);
                cmd.Parameters.AddWithValue("$ip", record.TargetIp);
                AddNullableInt(cmd, "$port", record.Port);
                cmd.Parameters.AddWithValue("$ping", record.PingSuccess ? 1 : 0);
                AddNullableInt(cmd, "$latency", record.LatencyMs is null ? null : (int)record.LatencyMs.Value);
                cmd.Parameters.AddWithValue("$portResult", (int)record.PortResult);
                cmd.Parameters.AddWithValue("$status", (int)record.Status);
                cmd.Parameters.AddWithValue("$error", (object?)record.ErrorSummary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$consecutive", record.ConsecutiveFailures);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Degrade("append-detect", ex.Message);
                return false;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> AppendAlertRecordAsync(
            AlertRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
                {
                    Degrade("append-alert", "持久化不可用");
                    return false;
                }

                await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    INSERT INTO {AlertTableName}
                        (timestamp, target_id, target_ip, port, consecutive_failures, error_summary, delivery_methods, last_failure_at)
                    VALUES ($ts, $targetId, $ip, $port, $consecutive, $error, $methods, $lastFailure);
                    """;
                cmd.Parameters.AddWithValue("$ts", record.Timestamp.Ticks);
                cmd.Parameters.AddWithValue("$targetId", record.TargetId);
                cmd.Parameters.AddWithValue("$ip", record.TargetIp);
                AddNullableInt(cmd, "$port", record.Port);
                cmd.Parameters.AddWithValue("$consecutive", record.ConsecutiveFailures);
                cmd.Parameters.AddWithValue("$error", (object?)record.ErrorSummary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$methods", (int)record.DeliveryMethods);
                cmd.Parameters.AddWithValue("$lastFailure", record.LastFailureAt is { } at ? at.Ticks : DBNull.Value);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Degrade("append-alert", ex.Message);
                return false;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<IReadOnlyList<DetectionRecord>> LoadDetectionRecordsAsync(
            DateTime since, int? maxCount = null, string? targetId = null,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var list = new List<DetectionRecord>();
                if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
                {
                    return list;
                }

                await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                // maxCount 取“最近 N 条”：先按时间倒序截断，读完后翻正为升序返回
                cmd.CommandText = $"""
                    SELECT timestamp, target_id, target_ip, port, ping_success, latency_ms,
                           port_result, status, error_summary, consecutive_failures
                    FROM {DetectTableName}
                    WHERE timestamp >= $since
                      AND ($targetId IS NULL OR target_id = $targetId)
                    ORDER BY timestamp DESC
                    {(maxCount is > 0 ? "LIMIT $limit" : string.Empty)};
                    """;
                cmd.Parameters.AddWithValue("$since", since.Ticks);
                cmd.Parameters.AddWithValue("$targetId", (object?)targetId ?? DBNull.Value);
                if (maxCount is > 0)
                {
                    cmd.Parameters.AddWithValue("$limit", maxCount.Value);
                }

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(new DetectionRecord
                    {
                        Timestamp = new DateTime(reader.GetInt64(0)),
                        TargetId = reader.GetString(1),
                        TargetIp = reader.GetString(2),
                        Port = ReadNullableInt(reader, 3),
                        PingSuccess = reader.GetInt32(4) != 0,
                        LatencyMs = ReadNullableInt(reader, 5),
                        PortResult = (PortProbeStatus)reader.GetInt32(6),
                        Status = (DetectionStatus)reader.GetInt32(7),
                        ErrorSummary = reader.IsDBNull(8) ? null : reader.GetString(8),
                        ConsecutiveFailures = reader.GetInt32(9),
                    });
                }

                list.Reverse();
                return list;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Degrade("load-detect", ex.Message);
                return [];
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<IReadOnlyList<AlertRecord>> LoadAlertRecordsAsync(
            DateTime since, string? targetId = null, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var list = new List<AlertRecord>();
                if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
                {
                    return list;
                }

                await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT timestamp, target_id, target_ip, port, consecutive_failures, error_summary, delivery_methods, last_failure_at
                    FROM {AlertTableName}
                    WHERE timestamp >= $since
                      AND ($targetId IS NULL OR target_id = $targetId)
                    ORDER BY timestamp ASC;
                    """;
                cmd.Parameters.AddWithValue("$since", since.Ticks);
                cmd.Parameters.AddWithValue("$targetId", (object?)targetId ?? DBNull.Value);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(new AlertRecord
                    {
                        Timestamp = new DateTime(reader.GetInt64(0)),
                        TargetId = reader.GetString(1),
                        TargetIp = reader.GetString(2),
                        Port = ReadNullableInt(reader, 3),
                        ConsecutiveFailures = reader.GetInt32(4),
                        ErrorSummary = reader.IsDBNull(5) ? null : reader.GetString(5),
                        DeliveryMethods = (AlertDeliveryMethods)reader.GetInt32(6),
                        LastFailureAt = reader.IsDBNull(7) ? null : new DateTime(reader.GetInt64(7)),
                    });
                }

                return list;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Degrade("load-alert", ex.Message);
                return [];
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<int?> LoadLatestConsecutiveFailuresAsync(
            string targetId, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT consecutive_failures FROM {DetectTableName}
                    WHERE target_id = $targetId
                    ORDER BY timestamp DESC, id DESC LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$targetId", targetId);
                var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return value is null or DBNull ? null : Convert.ToInt32(value);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Degrade("load-consecutive", ex.Message);
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<int> DeleteDetectionRecordsBeforeAsync(
            DateTime before, CancellationToken cancellationToken = default)
            => DeleteBeforeAsync(DetectTableName, before, "delete-detect", cancellationToken);

        public Task<int> DeleteAlertRecordsBeforeAsync(
            DateTime before, CancellationToken cancellationToken = default)
            => DeleteBeforeAsync(AlertTableName, before, "delete-alert", cancellationToken);

        public Task<int> ClearDetectionRecordsAsync(CancellationToken cancellationToken = default)
            => ClearAsync(DetectTableName, "clear-detect", cancellationToken);

        public Task<int> ClearAlertRecordsAsync(CancellationToken cancellationToken = default)
            => ClearAsync(AlertTableName, "clear-alert", cancellationToken);

        public async Task SealAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_dataSource is null)
                {
                    return;
                }

                if (_persistent)
                {
                    try
                    {
                        // WAL 落盘，确保 -wal 中无未合并数据后再复制文件
                        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger?.LogWarning(ex, "WAL checkpoint 失败，迁移前数据库可能残留 -wal 文件");
                    }
                }

                // 释放连接池，避免文件被占用导致复制/删除失败
                SqliteConnection.ClearPool(new SqliteConnection(BuildConnectionString()));
                _persistent = false;
                _dataSource = null;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<int> DeleteBeforeAsync(
            string table, DateTime before, string operation, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
                {
                    Degrade(operation, "持久化不可用");
                    return 0;
                }

                await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DELETE FROM {table} WHERE timestamp < $before;";
                cmd.Parameters.AddWithValue("$before", before.Ticks);
                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Degrade(operation, ex.Message);
                return 0;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<int> ClearAsync(string table, string operation, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
                {
                    Degrade(operation, "持久化不可用");
                    return 0;
                }

                await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DELETE FROM {table};";
                var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                // 重置自增序列，配合“清空”语义（VACUUM 在降级/锁风险下不做强制）。
                // sqlite_sequence 仅在首次插入 AUTOINCREMENT 表后才存在，故限定有删除时才执行。
                if (deleted > 0)
                {
                    await using var reset = conn.CreateCommand();
                    reset.CommandText = "DELETE FROM sqlite_sequence WHERE name = $name;";
                    reset.Parameters.AddWithValue("$name", table);
                    await reset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                return deleted;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Degrade(operation, ex.Message);
                return 0;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// 确认连接可用：未初始化则建表；已降级且过了冷却期则尝试恢复。调用方须持锁。
        /// </summary>
        private async Task<bool> EnsureReadyAsync(CancellationToken ct)
        {
            if (_persistent)
            {
                return true;
            }

            if (_dataSource is not null && DateTime.Now < _nextRetryAt)
            {
                return false;
            }

            return await InitializeCoreAsync(ct).ConfigureAwait(false);
        }

        private void Degrade(string operation, string reason)
        {
            SetPersistent(false);
            _logger?.LogWarning("持久化写入失败（{Operation}）：{Reason}，本条记录仅保留在内存", operation, reason);
            WriteFailed?.Invoke(this, new StoreWriteFailedEventArgs(operation, reason));
        }

        private void SetPersistent(bool value)
        {
            _persistent = value;
            if (!value)
            {
                _nextRetryAt = DateTime.Now.Add(RecoveryRetryDelay);
            }
        }

        private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new SqliteConnection(BuildConnectionString());
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return conn;
        }

        private string BuildConnectionString() =>
            new SqliteConnectionStringBuilder
            {
                DataSource = _dataSource ?? AppPaths.DatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
            }.ToString();

        private static void AddNullableInt(SqliteCommand cmd, string name, int? value) =>
            cmd.Parameters.AddWithValue(name, value.HasValue ? (object)value.Value : DBNull.Value);

        private static int? ReadNullableInt(SqliteDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}
