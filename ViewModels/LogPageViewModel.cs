using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 日志页视图模型（DESIGN 第四节 2）：保留期记录集合（受内存条数限制）、
    /// 状态 / 时间范围筛选、排序（默认时间降序，列头可切换）、CSV 导出（当前筛选结果）、
    /// 清空（二次确认，同清界面集合与数据库）与打开日志目录。
    /// </summary>
    public sealed partial class LogPageViewModel : PageViewModelBase
    {
        private readonly IConfigService _config;
        private readonly IStoreService _store;
        private readonly IExportService _export;
        private readonly IDialogService _dialog;

        /// <summary>批量装载期间抑制计数刷新，避免逐条 O(n²) 统计。</summary>
        private bool _loadingRecords;

        public LogPageViewModel(
            IRuntimeStatusService status,
            IConfigService config,
            IStoreService store,
            IExportService export,
            IDialogService dialog)
        {
            Status = status;
            _config = config;
            _store = store;
            _export = export;
            _dialog = dialog;
            status.RecordReceived += (_, record) => Append(record);

            StatusFilterOptions = new[]
            {
                new StatusFilterOption(null, "全部状态"),
                new StatusFilterOption(DetectionStatus.Success, "成功"),
                new StatusFilterOption(DetectionStatus.PingFailed, "Ping 失败"),
                new StatusFilterOption(DetectionStatus.PortFailed, "端口失败"),
                new StatusFilterOption(DetectionStatus.Error, "异常"),
            };
            _selectedStatusFilter = StatusFilterOptions[0];

            RecordsView = CollectionViewSource.GetDefaultView(Records);
            RecordsView.SortDescriptions.Add(
                new SortDescription(nameof(DetectionRecord.Timestamp), ListSortDirection.Descending));
            RecordsView.Filter = FilterRecord;

            Records.CollectionChanged += OnRecordsChanged;
            UpdateCountText();
        }

        public override string Key => "log";

        public override string Title => "检测日志";

        public IRuntimeStatusService Status { get; }

        /// <summary>界面记录集合（时间升序维护以便裁剪最旧记录，展示顺序由 <see cref="RecordsView"/> 决定）。</summary>
        public ObservableCollection<DetectionRecord> Records { get; } = new();

        /// <summary>表格绑定的视图：筛选 + 排序（列头点击可切换排序列）。</summary>
        public ICollectionView RecordsView { get; }

        /// <summary>保留期提示文案（DESIGN 第四节 2）。</summary>
        public string RetentionHint { get; private set; } = string.Empty;

        // ---- 筛选（DESIGN 第四节 2：按状态、按时间范围）----

        public IReadOnlyList<StatusFilterOption> StatusFilterOptions { get; }

        [ObservableProperty] private StatusFilterOption _selectedStatusFilter;

        /// <summary>起始日期（含当日），null 表示不限。</summary>
        [ObservableProperty] private DateTime? _fromDate;

        /// <summary>结束日期（含当日），null 表示不限。</summary>
        [ObservableProperty] private DateTime? _toDate;

        /// <summary>起止日期颠倒时的行内提示（此时筛选结果为空）。</summary>
        [ObservableProperty] private string _timeRangeError = string.Empty;

        /// <summary>总条数与筛选后条数文案。</summary>
        [ObservableProperty] private string _countText = string.Empty;

        /// <summary>操作结果提示（导出 / 清空），展示在筛选条下方。</summary>
        [ObservableProperty] private string _actionMessage = string.Empty;

        public override async Task ActivateAsync()
        {
            var since = _config.Current.RetentionStart;
            var limit = _config.Current.Storage.MaxInMemoryRecords;
            var loaded = await _store.LoadDetectionRecordsAsync(since, limit > 0 ? limit : null);

            _loadingRecords = true;
            Records.Clear();
            foreach (var record in loaded)
            {
                Records.Add(record);
            }
            _loadingRecords = false;

            RefreshView();

            RetentionHint = $"当前保留 {_config.Current.Storage.RetentionDays} 天，"
                          + $"早于 {since:yyyy-MM-dd} 的记录将被自动清理（数据库保留完整保留期内数据）";
            OnPropertyChanged(nameof(RetentionHint));
        }

        // ---- 导出（DESIGN 第十二节：当前筛选结果，UTF-8 BOM，完成后提示条数与路径）----

        [RelayCommand]
        private async Task ExportCsvAsync()
        {
            ActionMessage = string.Empty;
            var records = RecordsView.Cast<DetectionRecord>().ToList();
            if (records.Count == 0)
            {
                ActionMessage = "当前筛选条件下没有可导出的记录";
                return;
            }

            var filePath = _dialog.PickCsvSaveFile(_export.BuildDefaultFileName(_config.Current.Detection.TargetIp));
            if (filePath is null)
            {
                return;
            }

            try
            {
                var count = await _export.ExportDetectionRecordsAsync(records, filePath);
                ActionMessage = $"已导出 {count} 条记录到 {filePath}";
            }
            catch (Exception ex)
            {
                ActionMessage = "导出失败";
                _dialog.ShowError($"导出 CSV 失败：{ex.Message}\n\n目标文件：{filePath}");
            }
        }

        // ---- 清空（DESIGN 第四节 2：二次确认，同清界面集合与数据库）----

        [RelayCommand]
        private async Task ClearAsync()
        {
            if (!_dialog.Confirm(
                    "清空将同时删除界面上展示的记录与数据库中的全部检测历史，且不可恢复。\n\n确定要清空吗？",
                    "清空检测日志"))
            {
                return;
            }

            try
            {
                var cleared = await _store.ClearDetectionRecordsAsync();
                Records.Clear();
                ActionMessage = _store.IsPersistent
                    ? $"已清空：数据库删除 {cleared} 条，界面集合已同步清空"
                    : "界面集合已清空（持久化当前不可用，数据库未能删除）";
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"清空检测日志失败：{ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenLogDirectory()
        {
            try
            {
                var directory = AppPaths.EnsureLogDirectory();
                Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"无法打开日志目录：{ex.Message}");
            }
        }

        [RelayCommand]
        private void ResetFilter()
        {
            SelectedStatusFilter = StatusFilterOptions[0];
            FromDate = null;
            ToDate = null;
            ActionMessage = string.Empty;
        }

        partial void OnSelectedStatusFilterChanged(StatusFilterOption value) => RefreshView();

        partial void OnFromDateChanged(DateTime? value) => RefreshView();

        partial void OnToDateChanged(DateTime? value) => RefreshView();

        private bool FilterRecord(object item)
        {
            if (item is not DetectionRecord record)
            {
                return false;
            }

            if (SelectedStatusFilter.Status is { } status && record.Status != status)
            {
                return false;
            }

            if (FromDate is { } from && record.Timestamp < from.Date)
            {
                return false;
            }

            if (ToDate is { } to && record.Timestamp >= to.Date.AddDays(1))
            {
                return false;
            }

            return true;
        }

        private void RefreshView()
        {
            TimeRangeError = FromDate is { } from && ToDate is { } to && from.Date > to.Date
                ? "起始日期晚于结束日期，筛选结果为空"
                : string.Empty;
            RecordsView.Refresh();
            UpdateCountText();
        }

        private void Append(DetectionRecord record)
        {
            Records.Add(record);
            TrimToMemoryLimit();
        }

        private void OnRecordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_loadingRecords)
            {
                UpdateCountText();
            }
        }

        private void UpdateCountText()
        {
            var shown = Records.Count(FilterRecord);
            CountText = shown == Records.Count
                ? $"共 {Records.Count} 条记录"
                : $"共 {Records.Count} 条记录，筛选后 {shown} 条";
        }

        private void TrimToMemoryLimit()
        {
            var limit = _config.Current.Storage.MaxInMemoryRecords;
            if (limit <= 0)
            {
                return;
            }

            while (Records.Count > limit)
            {
                Records.RemoveAt(0);
            }
        }

        /// <summary>状态下拉选项：null 表示全部（记录只会落入四种检测状态，暂停 / 告警为界面状态不入库）。</summary>
        public sealed record StatusFilterOption(DetectionStatus? Status, string Label);
    }
}