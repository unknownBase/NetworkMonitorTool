using NetworkMonitorTool.Models;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 连续失败告警引擎（DESIGN 第八节）：以整体状态判定失败，连续失败累加、成功一次清零；
    /// 达到阈值触发一次并进入「已告警」去重状态，恢复成功后重置。
    /// 端口失败计入开关关闭时，端口失败既不累加也不清零（仅成功清零，DESIGN 第八节 1/2/4）。
    /// 告警阈值与计入规则每次判定读取当前配置，保存后立即用于后续判断（DESIGN 第五节 2）。
    /// </summary>
    public sealed class AlertService : IAlertService
    {
        private readonly IConfigService _config;
        private readonly IStoreService _store;
        private readonly ILogger<AlertService>? _logger;

        private readonly object _sync = new();
        private int _failures;
        private bool _alerted;
        private DetectionRecord? _lastFailure;

        public AlertService(
            IConfigService config,
            IStoreService store,
            ILogger<AlertService>? logger = null)
        {
            _config = config;
            _store = store;
            _logger = logger;
        }

        public int ConsecutiveFailures
        {
            get { lock (_sync) { return _failures; } }
        }

        public bool IsAlerting
        {
            get { lock (_sync) { return _alerted; } }
        }

        public event EventHandler<AlertRecord>? AlertTriggered;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var failures = await _store.LoadLatestConsecutiveFailuresAsync(cancellationToken)
                .ConfigureAwait(false);
            var recent = await _store.LoadDetectionRecordsAsync(
                _config.Current.RetentionStart, maxCount: 1, cancellationToken).ConfigureAwait(false);
            var last = recent.Count > 0 ? recent[^1] : null;

            lock (_sync)
            {
                _failures = failures ?? last?.ConsecutiveFailures ?? 0;
                _alerted = false;
                _lastFailure = _failures > 0 && last is not null && last.Status != DetectionStatus.Success
                    ? last
                    : null;
            }
        }

        public async Task OnMonitoringStartedAsync(CancellationToken cancellationToken = default)
        {
            AlertRecord? triggered;
            lock (_sync)
            {
                triggered = TryTriggerLocked(DateTime.Now);
            }

            if (triggered is not null)
            {
                await PublishAsync(triggered, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<DetectionRecord> EvaluateAsync(
            DetectionRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
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
                    _failures = 0;
                    _alerted = false;
                    _lastFailure = null;
                }
                else if (countsAsFailure)
                {
                    _failures++;
                    _lastFailure = record;
                    triggered = TryTriggerLocked(record.Timestamp);
                }

                count = _failures;
            }

            if (triggered is not null)
            {
                await PublishAsync(triggered, cancellationToken).ConfigureAwait(false);
            }

            return record.WithConsecutiveFailures(count);
        }

        /// <summary>调用方持有锁：越阈且未告警时生成告警记录并置去重状态，否则返回 null。</summary>
        private AlertRecord? TryTriggerLocked(DateTime timestamp)
        {
            var alertConfig = _config.Current.Alert;
            if (_alerted || _failures < alertConfig.ConsecutiveFailureThreshold)
            {
                return null;
            }

            _alerted = true;
            var target = _lastFailure?.TargetIp ?? _config.Current.Detection.TargetIp;
            return new AlertRecord
            {
                Timestamp = timestamp,
                TargetIp = target,
                Port = _lastFailure?.Port ?? _config.Current.Detection.Port,
                ConsecutiveFailures = _failures,
                ErrorSummary = _lastFailure?.ErrorSummary,
                LastFailureAt = _lastFailure?.Timestamp,
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
    }
}