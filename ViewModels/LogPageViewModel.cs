using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 日志页视图模型（DESIGN 第四节 2 的框架部分）：保留期记录集合、内存条数限制与保留期提示。
    /// 筛选 / 排序 / 导出 / 清空在 S5 补全，本迭代先打通「历史记录可见」这条主链路。
    /// </summary>
    public sealed partial class LogPageViewModel : PageViewModelBase
    {
        private readonly IConfigService _config;
        private readonly IStoreService _store;

        public LogPageViewModel(IRuntimeStatusService status, IConfigService config, IStoreService store)
        {
            Status = status;
            _config = config;
            _store = store;
            status.RecordReceived += (_, record) => Append(record);
        }

        public override string Key => "log";

        public override string Title => "检测日志";

        public IRuntimeStatusService Status { get; }

        /// <summary>界面记录集合（时间升序，受内存最大条数限制）。</summary>
        public ObservableCollection<DetectionRecord> Records { get; } = new();

        /// <summary>保留期提示文案（DESIGN 第四节 2）。</summary>
        public string RetentionHint { get; private set; } = string.Empty;

        [ObservableProperty] private string _pendingFeatureHint =
            "筛选、排序、导出 CSV 与清空将在 S5 迭代交付";

        private void Append(DetectionRecord record)
        {
            Records.Add(record);
            TrimToMemoryLimit();
        }

        public override async Task ActivateAsync()
        {
            var since = _config.Current.RetentionStart;
            var loaded = await _store.LoadDetectionRecordsAsync(since);

            Records.Clear();
            foreach (var record in loaded)
            {
                Records.Add(record);
            }

            TrimToMemoryLimit();

            RetentionHint = $"当前保留 {_config.Current.Storage.RetentionDays} 天，"
                          + $"早于 {since:yyyy-MM-dd} 的记录将被自动清理（数据库保留完整保留期内数据供导出）";
            OnPropertyChanged(nameof(RetentionHint));
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
    }
}
