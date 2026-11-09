using System.Collections.ObjectModel;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 告警历史页视图模型（DESIGN 第四节 3 的框架部分）：展示 alert_record 分表中的历史告警。
    /// 告警由 S6 引擎触发写入；弹窗 / 托盘通知（S7）、时间筛选与清空在后续迭代补全。
    /// </summary>
    public sealed partial class AlertHistoryViewModel : PageViewModelBase
    {
        private readonly IConfigService _config;
        private readonly IStoreService _store;

        public AlertHistoryViewModel(IConfigService config, IStoreService store)
        {
            _config = config;
            _store = store;
        }

        public override string Key => "alerts";

        public override string Title => "告警历史";

        /// <summary>告警记录集合（时间升序，与检测日志分表）。</summary>
        public ObservableCollection<AlertRecord> Records { get; } = new();

        /// <summary>保留期提示（与检测记录共用保留天数，DESIGN 第十一节 2）。</summary>
        public string RetentionHint { get; private set; } = string.Empty;

        public string PendingFeatureHint { get; } =
            "告警弹窗 / 托盘通知与本页筛选、清空在 S7 交付";

        public override async Task ActivateAsync()
        {
            var since = _config.Current.RetentionStart;
            var loaded = await _store.LoadAlertRecordsAsync(since);

            Records.Clear();
            foreach (var record in loaded)
            {
                Records.Add(record);
            }

            RetentionHint = $"当前保留 {_config.Current.Storage.RetentionDays} 天，"
                          + $"早于 {since:yyyy-MM-dd} 的告警记录将被自动清理";
            OnPropertyChanged(nameof(RetentionHint));
        }
    }
}
