using NetworkMonitorTool.Models;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 连续失败告警引擎（DESIGN 第八节、DESIGN_S16_MULTITARGET 第七节）：
    /// 以整体状态判定失败，连续失败累加、成功一次清零；达到阈值触发一次并进入「已告警」去重状态，
    /// 恢复成功后重置。S16 起计数、去重状态与最近失败记录均按目标（<see cref="DetectionRecord.TargetId"/>）独立维护。
    /// 端口失败计入开关关闭时，端口失败既不累加也不清零（仅成功清零，DESIGN 第八节 1/2/4）。
    /// 告警阈值与计入规则每次判定读取当前配置，保存后立即用于后续判断（DESIGN 第五节 2）。
    /// </summary>
    public sealed class AlertService : IAlertService
    {
        private readonly IConfigService _config;
        private readonly IStoreService _store;
        private readonly ILogger<AlertService>? _logger;

        private readonly object _sync = new();
        private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);
        private readonly HashSet<string> _alerted = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DetectionRecord> _lastFailure = new(StringComparer.Ordinal);

        public AlertService(
            IConfigService config,
            IStoreService store,
            ILogger<AlertService>? logger = null)
        {
            _config = config;
            _store = store;
            _logger = logger;
        }

        /// <summary>各目标连续失败次数的最大值（汇总展示用）。</summary>
        public int ConsecutiveFailures
        {
            get { lock (_sync) { return _failures.Count == 0 ? 0 : _failures.Values.Max(); } }
        }

        /// <summary>任一目标处于告警状态即为 true（汇总展示用）。</summary>
        public bool IsAlerting
        {
            get { lock (_sync) { return _alerted.Count > 0; } }
        }

        public int GetConsecutiveFailures(string targetId)
        {
            var key = NormalizeKey(targetId);
            lock (_sync)
            {
                return _failures.TryGetValue(key, out var count) ? count : 0;
            }
        }

        public bool IsAlertingFor(string targetId)
        {
            var key = NormalizeKey(targetId);
            lock (_sync)
            {
                return _alerted.Contains(key);
            }
        }

        public event EventHandler<AlertRecord>? AlertTriggered;

        /// <summary>
        /// 冷启动延续：按目标分别读取最近连续失败计数与最近失败记录（DESIGN 第八节 3）。
        /// 目标集合取当前配置；已删除目标的历史不再延续。
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var config = _config.Current;
            foreach (var target in config.Targets)
            {
                var failures = await _store.LoadLatestConsecutiveFailuresAsync(target.Id, cancellationToken)
                    .ConfigureAwait(false);
                var recent = await _store.LoadDetectionRecordsAsync(
                    config.RetentionStart, maxCount: 1, targetId: target.Id, cancellationToken)
                    .ConfigureAwait(false);
                var last = recent.Count > 0 ? recent[^1] : null;

                lock (_sync)
                {
                    _failures[target.Id] = failures ?? last?.ConsecutiveFailures ?? 0;
                    _alerted.Remove(target.Id);
                    if (_failures[target.Id] > 0 && last is not null && last.Status != DetectionStatus.Success)
                    {
                        _lastFailure[target.Id] = last;
                    }
                    else
                    {
                        _lastFailure.Remove(target.Id);
                    }
                }
            }
        }

        public async Task OnMonitoringStartedAsync(CancellationToken cancellationToken = default)
        {
            var triggered = new List<AlertRecord>();
            lock (_sync)
            {
                foreach (var target in _config.Current.Targets)
                {
                    var alert = TryTriggerLocked(target.Id, DateTime.Now);
                    if (alert is not null)
                    {
                        triggered.Add(alert);
                    }
                }
            }

            foreach (var alert in triggered)
            {
                await PublishAsync(alert, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<DetectionRecord> EvaluateAsync(
            DetectionRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            var key = NormalizeKey(record.TargetId);
            var alertConfig = _config.Current.Alert;
            var isSuccess = record.Status == DetectionStatus.Success;
            var countsAsFailure = !isSuccess
                && (record.Status != DetectionStatus.PortFailed || alertConfig.CountPortFailure);

            AlertRecord? triggered = null;
            int count;
            lock (_sync)
            {
                if (isSuccess)
                {
                    _failures.Remove(key);
                    _alerted.Remove(key);
                    _lastFailure.Remove(key);
                }
                else if (countsAsFailure)
                {
                    var failures = (_failures.TryGetValue(key, out var current) ? current : 0) + 1;
                    _failures[key] = failures;
                    _lastFailure[key] = record;
                    triggered = TryTriggerLocked(key, record.Timestamp);
                }

                count = _failures.TryGetValue(key, out var value) ? value : 0;
            }

            if (triggered is not null)
            {
                await PublishAsync(triggered, cancellationToken).ConfigureAwait(false);
            }

            return record.WithConsecutiveFailures(count);
        }

        /// <summary>调用方持有锁：越阈且未告警时生成告警记录并置去重状态，否则返回 null。</summary>
        private AlertRecord? TryTriggerLocked(string targetId, DateTime timestamp)
        {
            var alertConfig = _config.Current.Alert;
            var failures = _failures.TryGetValue(targetId, out var count) ? count : 0;
            if (_alerted.Contains(targetId) || failures < alertConfig.ConsecutiveFailureThreshold)
            {
                return null;
            }

            _alerted.Add(targetId);
            var lastFailure = _lastFailure.TryGetValue(targetId, out var record) ? record : null;
            var target = _config.Current.FindTarget(targetId);
            return new AlertRecord
            {
                Timestamp = timestamp,
                TargetId = targetId,
                TargetIp = lastFailure?.TargetIp ?? target?.TargetIp ?? string.Empty,
                Port = lastFailure?.Port ?? target?.Port,
                ConsecutiveFailures = failures,
                ErrorSummary = lastFailure?.ErrorSummary,
                LastFailureAt = lastFailure?.Timestamp,
                DeliveryMethods = (alertConfig.PopupEnabled ? AlertDeliveryMethods.Popup : 0)
                    | (alertConfig.TrayNotifyEnabled ? AlertDeliveryMethods.TrayNotify : 0),
            };
        }

        /// <summary>写入告警历史后广播；写库失败已降级，不阻断告警与调度。</summary>
        private async Task PublishAsync(AlertRecord record, CancellationToken cancellationToken)
        {
            if (!await _store.AppendAlertRecordAsync(record, cancellationToken).ConfigureAwait(false))
            {
                _logger?.LogWarning("告警记录未入库，已降级为仅内存（{Time:HH:mm:ss}）", record.Timestamp);
            }

            AlertTriggered?.Invoke(this, record);
        }

        /// <summary>空 Id（理论上不出现）归一为迁移目标，避免报文丢失计数。</summary>
        private static string NormalizeKey(string? targetId)
            => string.IsNullOrEmpty(targetId) ? TargetConfig.LegacyTargetId : targetId;
    }
}
