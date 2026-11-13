using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NetworkMonitorTool.Converters;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 单个目标的运行期状态（S16）：最近记录、连续失败计数与告警标记，
    /// 供仪表盘目标列表、工具条目标选择与顶部汇总共用（DESIGN_S16_MULTITARGET 第八节）。
    /// 属性变更均在界面线程发生（由 <see cref="RuntimeStatusService"/> 编组）。
    /// </summary>
    public sealed partial class TargetStatus : ObservableObject
    {
        public TargetStatus(string targetId, string displayName, string address)
        {
            TargetId = targetId;
            _displayName = displayName;
            _address = address;
        }

        /// <summary>目标标识（对应 <see cref="TargetConfig.Id"/>）。</summary>
        public string TargetId { get; }

        /// <summary>界面展示名（名称非空取名称，否则取地址）。</summary>
        [ObservableProperty] private string _displayName;

        /// <summary>目标地址 IP[:端口]。</summary>
        [ObservableProperty] private string _address;

        /// <summary>该目标最近一条检测记录，未检测过时为 null。</summary>
        [ObservableProperty] private DetectionRecord? _lastRecord;

        /// <summary>该目标当前连续失败次数（由告警引擎按目标分账）。</summary>
        [ObservableProperty] private int _consecutiveFailures;

        /// <summary>该目标是否处于「已告警」状态（恢复成功后重置）。</summary>
        [ObservableProperty] private bool _isAlerting;

        /// <summary>目标状态：未检测为灰、告警中优先、否则取最近检测结果。</summary>
        public DetectionStatus Status => LastRecord is null
            ? DetectionStatus.Paused
            : IsAlerting
                ? DetectionStatus.Alerting
                : LastRecord.Status;

        /// <summary>目标状态文案。</summary>
        public string StatusText => LastRecord is null
            ? "未检测"
            : IsAlerting
                ? "告警中"
                : DetectionStatusToTextConverter.GetText(LastRecord.Status);

        /// <summary>目标状态取色。</summary>
        public Brush StatusBrush => DetectionStatusToBrushConverter.GetBrush(Status);

        /// <summary>最近延迟文案。</summary>
        public string LatencyText => LastRecord?.LatencyMs is long ms ? $"{ms} ms" : "—";

        /// <summary>最近检测时间文案。</summary>
        public string LastDetectionText => LastRecord?.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无记录";

        /// <summary>连续失败次数文案。</summary>
        public string ConsecutiveFailuresText => $"{ConsecutiveFailures} 次";

        partial void OnLastRecordChanged(DetectionRecord? value) => NotifyPresentation();

        partial void OnConsecutiveFailuresChanged(int value) => NotifyPresentation();

        partial void OnIsAlertingChanged(bool value) => NotifyPresentation();

        partial void OnDisplayNameChanged(string value) => NotifyPresentation();

        private void NotifyPresentation()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(LatencyText));
            OnPropertyChanged(nameof(LastDetectionText));
            OnPropertyChanged(nameof(ConsecutiveFailuresText));
        }
    }

    /// <summary>
    /// <see cref="IRuntimeStatusService"/> 的默认实现：订阅调度、告警与存储事件，
    /// 统一编组到界面线程后维护各目标状态与汇总状态。
    /// 仅在主程序（AddPresentationCore）注册，控制台验证工具不涉及界面状态。
    /// </summary>
    public sealed partial class RuntimeStatusService : ObservableObject, IRuntimeStatusService
    {
        private readonly ISchedulerService _scheduler;
        private readonly IAlertService _alerts;
        private readonly IStoreService _store;
        private readonly IConfigService _config;
        private readonly Dispatcher _dispatcher;
        private readonly Dictionary<string, TargetStatus> _statusById = new(StringComparer.Ordinal);

        [ObservableProperty]
        private SchedulerState _state;

        [ObservableProperty]
        private string? _selectedTargetId;

        [ObservableProperty]
        private bool _isPersistent = true;

        [ObservableProperty]
        private string? _persistenceHint;

        public RuntimeStatusService(
            ISchedulerService scheduler,
            IAlertService alerts,
            IStoreService store,
            IConfigService config,
            Dispatcher dispatcher)
        {
            _scheduler = scheduler;
            _alerts = alerts;
            _store = store;
            _config = config;
            _dispatcher = dispatcher;

            _state = scheduler.State;
            _isPersistent = store.IsPersistent;
            RebuildTargets();

            scheduler.StateChanged += HandleSchedulerStateChanged;
            scheduler.DetectionCompleted += HandleDetectionCompleted;
            alerts.AlertTriggered += HandleAlertTriggered;
            store.WriteFailed += HandleWriteFailed;
            config.ConfigChanged += (_, _) => OnUi(RebuildTargets);
        }

        public event EventHandler<DetectionRecord>? RecordReceived;

        /// <summary>各目标运行期状态（顺序与配置目标列表一致）。</summary>
        public ObservableCollection<TargetStatus> TargetStatuses { get; } = new();

        IReadOnlyList<TargetStatus> IRuntimeStatusService.Targets => TargetStatuses;

        public DetectionStatus Status
        {
            get
            {
                if (State == SchedulerState.Paused)
                {
                    return DetectionStatus.Paused;
                }

                if (TargetStatuses.Any(t => t.IsAlerting))
                {
                    return DetectionStatus.Alerting;
                }

                // 汇总取最差：异常 > Ping 失败 > 端口失败 > 成功（未检测的目标不参与）
                return TargetStatuses
                    .Where(t => t.LastRecord is not null)
                    .Select(t => t.LastRecord!.Status)
                    .DefaultIfEmpty(DetectionStatus.Success)
                    .OrderByDescending(Severity)
                    .First();
            }
        }

        public string StatusText => State switch
        {
            SchedulerState.Stopped => "未运行",
            SchedulerState.Paused => "暂停中",
            _ => TargetStatuses.All(t => t.LastRecord is null)
                ? "未检测"
                : DetectionStatusToTextConverter.GetText(Status),
        };

        public Brush StatusBrush => State switch
        {
            // 未运行与暂停中同为灰色界面态（DESIGN 第十节）
            SchedulerState.Stopped or SchedulerState.Paused =>
                DetectionStatusToBrushConverter.GetBrush(DetectionStatus.Paused),
            // 运行中但尚无任何目标检测结果：与「未检测」文案一致取灰色
            _ when TargetStatuses.All(t => t.LastRecord is null) =>
                DetectionStatusToBrushConverter.GetBrush(DetectionStatus.Paused),
            _ => DetectionStatusToBrushConverter.GetBrush(Status),
        };

        public string LastDetectionText
        {
            get
            {
                var latest = LatestRecord();
                return latest?.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无记录";
            }
        }

        public string LatencyText => LatestRecord()?.LatencyMs is long ms ? $"{ms} ms" : "—";

        public string ConsecutiveFailuresText
        {
            get
            {
                if (TargetStatuses.All(t => t.LastRecord is null) && State == SchedulerState.Stopped)
                {
                    return "—";
                }

                var max = TargetStatuses.Count == 0 ? 0 : TargetStatuses.Max(t => t.ConsecutiveFailures);
                return $"{max} 次";
            }
        }

        public bool IsAlerting => TargetStatuses.Any(t => t.IsAlerting);

        public TargetStatus? SelectedTarget => SelectedTargetId is not null
            && _statusById.TryGetValue(SelectedTargetId, out var status)
                ? status
                : null;

        public string SelectedStatusText => SelectedTarget?.StatusText ?? StatusText;

        public Brush SelectedStatusBrush => SelectedTarget is null
            ? StatusBrush
            : SelectedTarget.StatusBrush;

        public string SelectedLatencyText => SelectedTarget?.LatencyText ?? LatencyText;

        public string SelectedConsecutiveFailuresText
            => SelectedTarget?.ConsecutiveFailuresText ?? ConsecutiveFailuresText;

        public bool SelectedIsAlerting => SelectedTarget?.IsAlerting ?? IsAlerting;

        public async Task InitializeAsync(DateTime since, CancellationToken cancellationToken = default)
        {
            foreach (var target in _config.Current.Targets)
            {
                var records = await _store
                    .LoadDetectionRecordsAsync(since, maxCount: 1, targetId: target.Id, cancellationToken)
                    .ConfigureAwait(false);
                var last = records.Count > 0 ? records[^1] : null;
                var failures = await _store
                    .LoadLatestConsecutiveFailuresAsync(target.Id, cancellationToken)
                    .ConfigureAwait(false) ?? last?.ConsecutiveFailures ?? 0;
                var alerting = _alerts.IsAlertingFor(target.Id);

                OnUi(() =>
                {
                    var status = EnsureTarget(target.Id, target.DisplayName, target.Address);
                    status.LastRecord = last;
                    status.ConsecutiveFailures = failures;
                    status.IsAlerting = alerting;
                });
            }

            OnUi(() =>
            {
                IsPersistent = _store.IsPersistent;
                NotifyPresentation();
            });
        }

        partial void OnStateChanged(SchedulerState value) => NotifyPresentation();

        partial void OnSelectedTargetIdChanged(string? value) => NotifyPresentation();

        partial void OnIsPersistentChanged(bool value)
        {
            if (value)
            {
                // 连接恢复后清除降级提示（冷却重试成功后由后续记录回写状态）
                PersistenceHint = null;
            }

            NotifyPresentation();
        }

        private void HandleSchedulerStateChanged(object? sender, SchedulerState state)
            => OnUi(() =>
            {
                State = state;
                NotifyPresentation();
            });

        private void HandleDetectionCompleted(object? sender, DetectionRecord record) => OnUi(() =>
        {
            var target = _config.Current.FindTarget(record.TargetId);
            var status = EnsureTarget(
                record.TargetId,
                target?.DisplayName ?? record.TargetIp,
                target?.Address ?? record.TargetIp);

            status.LastRecord = record;
            status.ConsecutiveFailures = record.ConsecutiveFailures;
            status.IsAlerting = _alerts.IsAlertingFor(record.TargetId);

            // 写库成功即代表持久化可用（含降级后的自动恢复）
            if (_store.IsPersistent)
            {
                IsPersistent = true;
            }

            NotifyPresentation();
            RecordReceived?.Invoke(this, record);
        });

        private void HandleAlertTriggered(object? sender, AlertRecord record) => OnUi(() =>
        {
            var target = _config.Current.FindTarget(record.TargetId);
            var status = EnsureTarget(
                record.TargetId,
                target?.DisplayName ?? record.TargetIp,
                target?.Address ?? record.TargetIp);
            status.IsAlerting = true;
            status.ConsecutiveFailures = record.ConsecutiveFailures;
            NotifyPresentation();
        });

        private void HandleWriteFailed(object? sender, StoreWriteFailedEventArgs e) => OnUi(() =>
        {
            IsPersistent = false;
            PersistenceHint = $"持久化异常（{e.Operation}）：{e.Reason}，记录暂存内存，检测不中断";
        });

        /// <summary>按配置目标列表重建运行期状态集合：保留已有实例以免丢失运行中的计数与最近记录。</summary>
        private void RebuildTargets()
        {
            var config = _config.Current;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var target in config.Targets)
            {
                seen.Add(target.Id);
                var status = EnsureTarget(target.Id, target.DisplayName, target.Address);
                status.DisplayName = target.DisplayName;
                status.Address = target.Address;
            }

            foreach (var stale in _statusById.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _statusById.Remove(stale);
            }

            // 保持与配置同序，界面下拉与列表顺序稳定
            TargetStatuses.Clear();
            foreach (var target in config.Targets)
            {
                if (_statusById.TryGetValue(target.Id, out var status))
                {
                    TargetStatuses.Add(status);
                }
            }

            if (SelectedTargetId is not null && !_statusById.ContainsKey(SelectedTargetId))
            {
                SelectedTargetId = null;
            }

            NotifyPresentation();
        }

        private TargetStatus EnsureTarget(string targetId, string displayName, string address)
        {
            if (_statusById.TryGetValue(targetId, out var status))
            {
                return status;
            }

            status = new TargetStatus(targetId, displayName, address);
            status.PropertyChanged += (_, _) => NotifyPresentation();
            _statusById[targetId] = status;

            // 运行期出现配置外目标（理论上仅历史数据）时也纳入展示
            if (!TargetStatuses.Any(t => t.TargetId == targetId)
                && _config.Current.FindTarget(targetId) is null)
            {
                TargetStatuses.Add(status);
            }

            return status;
        }

        private DetectionRecord? LatestRecord()
            => TargetStatuses
                .Select(t => t.LastRecord)
                .OfType<DetectionRecord>()
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefault();

        /// <summary>汇总「最差状态」排序：异常 &gt; Ping 失败 &gt; 端口失败 &gt; 成功。</summary>
        private static int Severity(DetectionStatus status) => status switch
        {
            DetectionStatus.Error => 4,
            DetectionStatus.PingFailed => 3,
            DetectionStatus.PortFailed => 2,
            DetectionStatus.Alerting => 5,
            _ => 1,
        };

        private void NotifyPresentation()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(LastDetectionText));
            OnPropertyChanged(nameof(LatencyText));
            OnPropertyChanged(nameof(ConsecutiveFailuresText));
            OnPropertyChanged(nameof(IsAlerting));
            OnPropertyChanged(nameof(SelectedTarget));
            OnPropertyChanged(nameof(SelectedStatusText));
            OnPropertyChanged(nameof(SelectedStatusBrush));
            OnPropertyChanged(nameof(SelectedLatencyText));
            OnPropertyChanged(nameof(SelectedConsecutiveFailuresText));
            OnPropertyChanged(nameof(SelectedIsAlerting));
        }

        /// <summary>后台线程事件统一编组到界面线程，避免绑定集合跨线程访问异常。</summary>
        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _dispatcher.BeginInvoke(action);
        }
    }
}
