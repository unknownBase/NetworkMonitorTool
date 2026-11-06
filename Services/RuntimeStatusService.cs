using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NetworkMonitorTool.Converters;
using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// <see cref="IRuntimeStatusService"/> 的默认实现：订阅调度与存储事件，
    /// 统一编组到界面线程后暴露可绑定属性。
    /// 仅在主程序（AddPresentationCore）注册，控制台验证工具不涉及界面状态。
    /// </summary>
    public sealed partial class RuntimeStatusService : ObservableObject, IRuntimeStatusService
    {
        private readonly ISchedulerService _scheduler;
        private readonly IStoreService _store;
        private readonly Dispatcher _dispatcher;

        [ObservableProperty]
        private SchedulerState _state;

        [ObservableProperty]
        private DetectionRecord? _lastRecord;

        [ObservableProperty]
        private int _consecutiveFailures;

        [ObservableProperty]
        private bool _isPersistent = true;

        [ObservableProperty]
        private string? _persistenceHint;

        public RuntimeStatusService(
            ISchedulerService scheduler,
            IStoreService store,
            Dispatcher dispatcher)
        {
            _scheduler = scheduler;
            _store = store;
            _dispatcher = dispatcher;

            _state = scheduler.State;
            _isPersistent = store.IsPersistent;

            scheduler.StateChanged += HandleSchedulerStateChanged;
            scheduler.DetectionCompleted += HandleDetectionCompleted;
            store.WriteFailed += HandleWriteFailed;
        }

        public event EventHandler<DetectionRecord>? RecordReceived;

        public DetectionStatus Status => State == SchedulerState.Paused
            ? DetectionStatus.Paused
            : LastRecord?.Status ?? DetectionStatus.Success;

        public string StatusText => State switch
        {
            SchedulerState.Stopped => "未运行",
            SchedulerState.Paused => "暂停中",
            _ => DetectionStatusToTextConverter.GetText(Status),
        };

        public Brush StatusBrush => State switch
        {
            // 未运行与暂停中同为灰色界面态（DESIGN 第十节）
            SchedulerState.Stopped or SchedulerState.Paused =>
                DetectionStatusToBrushConverter.GetBrush(DetectionStatus.Paused),
            _ => DetectionStatusToBrushConverter.GetBrush(Status),
        };

        public string LastDetectionText => LastRecord?.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无记录";

        public string LatencyText => LastRecord?.LatencyMs is long ms ? $"{ms} ms" : "—";

        public string ConsecutiveFailuresText => LastRecord is null && State == SchedulerState.Stopped
            ? "—"
            : $"{ConsecutiveFailures} 次";

        public async Task InitializeAsync(DateTime since, CancellationToken cancellationToken = default)
        {
            var records = await _store
                .LoadDetectionRecordsAsync(since, maxCount: 1, cancellationToken)
                .ConfigureAwait(false);
            var last = records.Count > 0 ? records[^1] : null;
            var failures = await _store
                .LoadLatestConsecutiveFailuresAsync(cancellationToken)
                .ConfigureAwait(false) ?? last?.ConsecutiveFailures ?? 0;

            OnUi(() =>
            {
                LastRecord = last;
                ConsecutiveFailures = failures;
                IsPersistent = _store.IsPersistent;
            });
        }

        partial void OnStateChanged(SchedulerState value) => NotifyPresentation();

        partial void OnLastRecordChanged(DetectionRecord? value) => NotifyPresentation();

        partial void OnConsecutiveFailuresChanged(int value) => NotifyPresentation();

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
            => OnUi(() => State = state);

        private void HandleDetectionCompleted(object? sender, DetectionRecord record) => OnUi(() =>
        {
            LastRecord = record;
            ConsecutiveFailures = record.ConsecutiveFailures;

            // 写库成功即代表持久化可用（含降级后的自动恢复）
            if (_store.IsPersistent)
            {
                IsPersistent = true;
            }

            RecordReceived?.Invoke(this, record);
        });

        private void HandleWriteFailed(object? sender, StoreWriteFailedEventArgs e) => OnUi(() =>
        {
            IsPersistent = false;
            PersistenceHint = $"持久化异常（{e.Operation}）：{e.Reason}，记录暂存内存，检测不中断";
        });

        private void NotifyPresentation()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(LastDetectionText));
            OnPropertyChanged(nameof(LatencyText));
            OnPropertyChanged(nameof(ConsecutiveFailuresText));
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
