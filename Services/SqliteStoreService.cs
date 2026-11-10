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
                            target_ip            TEXT    NOT NULL,
                            port                 INTEGER NULL,
                            ping_success         INTEGER NOT NULL,
                            latency_ms           INTEGER NULL,
                            port_result          INTEGER NOT NULL,
                            status               INTEGER NOT NULL,
                            error_summary        TEXT    NULL,
                            consecutive_failures INTEGER NOT NULL DEFAULT 0
                        );
                        CREATE INDEX IF NOT EXISTS idx_{DetectTableName}_timestamp
                            ON {DetectTableName}(timestamp);
                        """;
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"""
                        CREATE TABLE IF NOT EXISTS {AlertTableName} (
                            id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                            timestamp            INTEGER NOT NULL,
                            target_ip            TEXT    NOT NULL,
                            port                 INTEGER NULL,
                            consecutive_failures INTEGER NOT NULL,
                            error_summary        TEXT    NULL,
                            delivery_methods     INTEGER NOT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_{AlertTableName}_timestamp
                            ON {AlertTableName}(timestamp);
                        """;
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // S7：旧库补充最近失败时间列（新库同样经此路径补齐，PRAGMA 探测保证幂等）
                await using (var probe = conn.CreateCommand())
                {
                    probe.CommandText = $"PRAGMA table_info({AlertTableName});";
                    var hasColumn = false;
                    await using (var reader = await probe.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        {
                            if (reader.GetString(1) == "last_failure_at")
                            {
                                hasColumn = true;
                                break;
                            }
                        }
                    }

                    if (!hasColumn)
                    {
                        await using var alter = conn.CreateCommand();
                        alter.CommandText = $"ALTER TABLE {AlertTableName} ADD COLUMN last_failure_at INTEGER NULL;";
                        await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
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
                        (timestamp, target_ip, port, ping_success, latency_ms,
                         port_result, status, error_summary, consecutive_failures)
                    VALUES ($ts, $ip, $port, $ping, $latency, $portResult, $status, $error, $consecutive);
                    """;
                cmd.Parameters.AddWithValue("$ts", record.Timestamp.Ticks);
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
                        (timestamp, target_ip, port, consecutive_failures, error_summary, delivery_methods, last_failure_at)
                    VALUES ($ts, $ip, $port, $consecutive, $error, $methods, $lastFailure);
                    """;
                cmd.Parameters.AddWithValue("$ts", record.Timestamp.Ticks);
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
            DateTime since, int? maxCount = null, CancellationToken cancellationToken = default)
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
                    SELECT timestamp, target_ip, port, ping_success, latency_ms,
                           port_result, status, error_summary, consecutive_failures
                    FROM {DetectTableName}
                    WHERE timestamp >= $since
                    ORDER BY timestamp DESC
                    {(maxCount is > 0 ? "LIMIT $limit" : string.Empty)};
                    """;
                cmd.Parameters.AddWithValue("$since", since.Ticks);
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
                        TargetIp = reader.GetString(1),
                        Port = ReadNullableInt(reader, 2),
                        PingSuccess = reader.GetInt32(3) != 0,
                        LatencyMs = ReadNullableInt(reader, 4),
                        PortResult = (PortProbeStatus)reader.GetInt32(5),
                        Status = (DetectionStatus)reader.GetInt32(6),
                        ErrorSummary = reader.IsDBNull(7) ? null : reader.GetString(7),
                        ConsecutiveFailures = reader.GetInt32(8),
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
            DateTime since, CancellationToken cancellationToken = default)
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
                    SELECT timestamp, target_ip, port, consecutive_failures, error_summary, delivery_methods, last_failure_at
                    FROM {AlertTableName}
                    WHERE timestamp >= $since
                    ORDER BY timestamp ASC;
                    """;
                cmd.Parameters.AddWithValue("$since", since.Ticks);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(new AlertRecord
                    {
                        Timestamp = new DateTime(reader.GetInt64(0)),
                        TargetIp = reader.GetString(1),
                        Port = ReadNullableInt(reader, 2),
                        ConsecutiveFailures = reader.GetInt32(3),
                        ErrorSummary = reader.IsDBNull(4) ? null : reader.GetString(4),
                        DeliveryMethods = (AlertDeliveryMethods)reader.GetInt32(5),
                        LastFailureAt = reader.IsDBNull(6) ? null : new DateTime(reader.GetInt64(6)),
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

        public async Task<int?> LoadLatestConsecutiveFailuresAsync(CancellationToken cancellationToken = default)
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
                    ORDER BY timestamp DESC, id DESC LIMIT 1;
                    """;
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
