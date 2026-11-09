using NetworkMonitorTool.Models;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 基于后台循环的调度实现：单循环串行触发检测，暂停用可等待门控阻断，
    /// 停止用取消令牌收口。事件在后台线程回调，订阅方（UI）自行编组到界面线程。
    /// 每条记录产生后先交告警引擎填写连续失败计数，再写入持久化存储后广播（DESIGN 第六节 2），
    /// 写库失败降级不中断调度。
    /// </summary>
    public sealed class SchedulerService : ISchedulerService
    {
        private readonly IDetectionService _detection;
        private readonly IAlertService _alerts;
        private readonly IStoreService _store;
        private readonly ILogger<SchedulerService>? _logger;

        private readonly object _sync = new();
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        // 暂停门控：处于“已恢复”时该 TCS 已完成，循环放行；暂停时替换为未完成的新实例。
        private TaskCompletionSource<bool> _resumeSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _paused;

        private DetectionOptions? _options;
        private DetectionInterval _interval;

        public SchedulerService(
            IDetectionService detection,
            IAlertService alerts,
            IStoreService store,
            ILogger<SchedulerService>? logger = null)
        {
            _detection = detection;
            _alerts = alerts;
            _store = store;
            _logger = logger;
            _resumeSignal.SetResult(true); // 初始为非暂停
            State = SchedulerState.Stopped;
        }

        public SchedulerState State { get; private set; }

        public event EventHandler<DetectionRecord>? DetectionCompleted;

        public event EventHandler<SchedulerState>? StateChanged;

        public async Task StartAsync(
            DetectionOptions options, DetectionInterval interval, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!interval.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), "检测间隔必须大于 0");
            }

            lock (_sync)
            {
                if (State != SchedulerState.Stopped)
                {
                    return; // 已在运行 / 暂停，忽略重复启动
                }

                _options = options;
                _interval = interval;
                _cts = new CancellationTokenSource();
                _paused = false;
                _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _resumeSignal.SetResult(true);

                var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
                _loopTask = Task.Run(() => RunLoopAsync(linked.Token));
                SetState(SchedulerState.Running);
            }

            // 冷启动计数已达阈值时按规则立即告警（DESIGN 第六节 1、第八节 3）
            await _alerts.OnMonitoringStartedAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            var options = _options!;
            var period = _interval.ToTimeSpan();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await WaitForResumeAsync(ct).ConfigureAwait(false);

                    var record = await _detection.DetectOnceAsync(options, ct).ConfigureAwait(false);
                    record = await _alerts.EvaluateAsync(record, ct).ConfigureAwait(false);
                    await PersistAsync(record, ct).ConfigureAwait(false);
                    DetectionCompleted?.Invoke(this, record);

                    await Task.Delay(period, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止 / 取消
            }
            catch (Exception ex)
            {
                _logger?.LogCritical(ex, "调度循环异常终止");
            }
            finally
            {
                lock (_sync)
                {
                    if (State != SchedulerState.Stopped)
                    {
                        SetState(SchedulerState.Stopped);
                    }
                }
            }
        }

        public void Pause()
        {
            lock (_sync)
            {
                if (State != SchedulerState.Running || _paused)
                {
                    return;
                }

                _paused = true;
                _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                SetState(SchedulerState.Paused);
            }
        }

        public void Resume()
        {
            lock (_sync)
            {
                if (State != SchedulerState.Paused || !_paused)
                {
                    return;
                }

                _paused = false;
                _resumeSignal.TrySetResult(true);
                SetState(SchedulerState.Running);
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            Task? loop;

            lock (_sync)
            {
                if (State == SchedulerState.Stopped && _loopTask is null)
                {
                    return;
                }

                cts = _cts;
                loop = _loopTask;
                _cts = null;
                _loopTask = null;
                // 解除暂停以便循环能响应取消
                _paused = false;
                _resumeSignal.TrySetResult(true);
                cts?.Cancel();
            }

            if (loop is not null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
            }

            cts?.Dispose();

            lock (_sync)
            {
                SetState(SchedulerState.Stopped);
            }
        }

        /// <summary>
        /// 写入持久化存储。失败已在存储层降级并触发 <see cref="IStoreService.WriteFailed"/>，
        /// 此处只记日志，绝不影响后续调度（DESIGN 第十四节 3）。
        /// </summary>
        private async Task PersistAsync(DetectionRecord record, CancellationToken ct)
        {
            try
            {
                if (!await _store.AppendDetectionRecordAsync(record, ct).ConfigureAwait(false))
                {
                    _logger?.LogWarning("检测记录未入库，已降级为仅内存（{Time:HH:mm:ss}）", record.Timestamp);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "写入检测记录时发生未预期异常，调度继续");
            }
        }

        private async Task WaitForResumeAsync(CancellationToken ct)
        {
            while (true)
            {
                Task signal;
                lock (_sync)
                {
                    signal = _resumeSignal.Task;
                }

                if (signal.IsCompleted)
                {
                    return;
                }

                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var completed = await Task.WhenAny(signal, Task.Delay(Timeout.Infinite, delayCts.Token))
                    .ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                if (completed == signal)
                {
                    return;
                }
            }
        }

        private void SetState(SchedulerState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(this, state);
        }

        public void Dispose()
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // 释放阶段忽略异常
            }
        }
    }
}
