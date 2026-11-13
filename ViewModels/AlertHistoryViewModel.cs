using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 告警历史页视图模型（DESIGN 第四节 3、DESIGN_S16_MULTITARGET 第八节 2）：alert_record 分表记录展示、
    /// 目标 / 时间范围筛选、排序（默认时间降序）、CSV 导出（当前筛选结果）、
    /// 清空（二次确认，同清界面集合与全部目标历史）；运行期新告警实时追加。
    /// </summary>
    public sealed partial class AlertHistoryViewModel : PageViewModelBase
    {
        private readonly IConfigService _config;
        private readonly IStoreService _store;
        private readonly IExportService _export;
        private readonly IDialogService _dialog;
        private readonly Dispatcher _dispatcher;

        /// <summary>批量装载期间抑制计数刷新，避免逐条 O(n²) 统计。</summary>
        private bool _loadingRecords;

        public AlertHistoryViewModel(
            IConfigService config,
            IStoreService store,
            IExportService export,
            IDialogService dialog,
            IAlertService alerts,
            Dispatcher dispatcher)
        {
            _config = config;
            _store = store;
            _export = export;
            _dialog = dialog;
            _dispatcher = dispatcher;

            // 告警事件可能来自调度后台线程，统一编组到界面线程后追加
            alerts.AlertTriggered += (_, record) => OnUi(() => Append(record));

            TargetFilterOptions = new ObservableCollection<TargetFilterOption>();
            _selectedTargetFilter = new TargetFilterOption(null, "全部目标");
            TargetFilterOptions.Add(_selectedTargetFilter);

            RecordsView = CollectionViewSource.GetDefaultView(Records);
            RecordsView.SortDescriptions.Add(
                new SortDescription(nameof(AlertRecord.Timestamp), ListSortDirection.Descending));
            RecordsView.Filter = FilterRecord;

            Records.CollectionChanged += OnRecordsChanged;
            UpdateCountText();
        }

        public override string Key => "alerts";

        public override string Title => "告警历史";

        /// <summary>界面告警集合（时间升序维护，展示顺序由 <see cref="RecordsView"/> 决定）。</summary>
        public ObservableCollection<AlertRecord> Records { get; } = new();

        /// <summary>表格绑定的视图：时间筛选 + 排序（列头点击可切换排序列）。</summary>
        public ICollectionView RecordsView { get; }

        /// <summary>保留期提示（与检测记录共用保留天数，DESIGN 第十一节 2）。</summary>
        public string RetentionHint { get; private set; } = string.Empty;

        // ---- 时间筛选（DESIGN 第四节 3）----

        /// <summary>目标下拉选项（S16）：首项为全部目标，其后为配置中的各目标。</summary>
        public ObservableCollection<TargetFilterOption> TargetFilterOptions { get; }

        [ObservableProperty] private TargetFilterOption _selectedTargetFilter;

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
            RebuildTargetFilterOptions();

            var since = _config.Current.RetentionStart;
            var loaded = await _store.LoadAlertRecordsAsync(since);

            _loadingRecords = true;
            Records.Clear();
            foreach (var record in loaded)
            {
                Records.Add(record);
            }
            _loadingRecords = false;

            RefreshView();

            RetentionHint = $"当前保留 {_config.Current.Storage.RetentionDays} 天，"
                          + $"早于 {since:yyyy-MM-dd} 的告警记录将被自动清理";
            OnPropertyChanged(nameof(RetentionHint));
        }

        // ---- 导出（当前筛选结果，UTF-8 BOM，完成后提示条数与路径）----

        [RelayCommand]
        private async Task ExportCsvAsync()
        {
            ActionMessage = string.Empty;
            var records = RecordsView.Cast<AlertRecord>().ToList();
            if (records.Count == 0)
            {
                ActionMessage = "当前筛选条件下没有可导出的告警记录";
                return;
            }

            var filePath = _dialog.PickCsvSaveFile(
                _export.BuildAlertDefaultFileName(CurrentExportTargetName()));
            if (filePath is null)
            {
                return;
            }

            try
            {
                var count = await _export.ExportAlertRecordsAsync(records, filePath);
                ActionMessage = $"已导出 {count} 条告警记录到 {filePath}";
            }
            catch (Exception ex)
            {
                ActionMessage = "导出失败";
                _dialog.ShowError($"导出告警 CSV 失败：{ex.Message}\n\n目标文件：{filePath}");
            }
        }

        // ---- 清空（二次确认，同清界面集合与数据库）----

        [RelayCommand]
        private async Task ClearAsync()
        {
            if (!_dialog.Confirm(
                    "清空将同时删除界面上展示的告警记录与数据库中【全部目标】的告警历史，且不可恢复。\n\n确定要清空吗？",
                    "清空告警历史"))
            {
                return;
            }

            try
            {
                var cleared = await _store.ClearAlertRecordsAsync();
                Records.Clear();
                ActionMessage = _store.IsPersistent
                    ? $"已清空：数据库删除 {cleared} 条，界面集合已同步清空"
                    : "界面集合已清空（持久化当前不可用，数据库未能删除）";
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"清空告警历史失败：{ex.Message}");
            }
        }

        [RelayCommand]
        private void ResetFilter()
        {
            // 首项始终为「全部目标」，重置回该项
            SelectedTargetFilter = TargetFilterOptions[0];
            FromDate = null;
            ToDate = null;
            ActionMessage = string.Empty;
        }

        partial void OnSelectedTargetFilterChanged(TargetFilterOption value) => RefreshView();

        partial void OnFromDateChanged(DateTime? value) => RefreshView();

        partial void OnToDateChanged(DateTime? value) => RefreshView();

        private bool FilterRecord(object item)
        {
            if (item is not AlertRecord record)
            {
                return false;
            }

            if (SelectedTargetFilter is { TargetId: { } targetId }
                && !string.Equals(record.TargetId, targetId, StringComparison.Ordinal))
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

        private void Append(AlertRecord record)
        {
            Records.Add(record);
        }

        /// <summary>重建目标下拉（配置变化后在重新激活页面时生效），尽量保留当前选择。</summary>
        private void RebuildTargetFilterOptions()
        {
            var previousId = SelectedTargetFilter?.TargetId;
            TargetFilterOptions.Clear();
            TargetFilterOptions.Add(new TargetFilterOption(null, "全部目标"));
            foreach (var target in _config.Current.Targets)
            {
                TargetFilterOptions.Add(new TargetFilterOption(target.Id, target.DisplayName));
            }

            SelectedTargetFilter = TargetFilterOptions.FirstOrDefault(o => o.TargetId == previousId)
                ?? TargetFilterOptions[0];
        }

        /// <summary>导出文件名中的目标标识：选定目标用其地址，全部目标统一以「多目标」命名。</summary>
        private string CurrentExportTargetName()
        {
            if (SelectedTargetFilter is not { TargetId: { } targetId })
            {
                return "多目标";
            }

            return _config.Current.FindTarget(targetId)?.Address ?? "目标";
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
                ? $"共 {Records.Count} 条告警记录"
                : $"共 {Records.Count} 条告警记录，筛选后 {shown} 条";
        }

        /// <summary>目标下拉选项：TargetId 为 null 表示全部目标（含已删除目标的历史需选「全部目标」）。</summary>
        public sealed record TargetFilterOption(string? TargetId, string Label);

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