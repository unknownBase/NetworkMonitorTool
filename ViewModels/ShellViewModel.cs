using CommunityToolkit.Mvvm.ComponentModel;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 主窗口壳视图模型（DESIGN 第二、三节）：导航项与当前页、全局状态引用、
    /// 上次停留页记忆，以及「有未保存修改时切页 / 关窗需确认」的统一收口。
    /// 检测的启停暂停恢复属于各页快捷操作与后续工具条，壳层不重复承载。
    /// </summary>
    public sealed partial class ShellViewModel : ObservableObject
    {
        /// <summary>无记忆时的默认落地页（DESIGN 第三节 2）。</summary>
        internal const string DefaultPageKey = "dashboard";

        public ShellViewModel(
            IRuntimeStatusService status,
            DashboardViewModel dashboard,
            LogPageViewModel log,
            AlertHistoryViewModel alerts,
            SettingsViewModel settings,
            AboutViewModel about)
        {
            Status = status;

            NavItems = new[]
            {
                new NavItem(dashboard.Key, dashboard.Title, dashboard),
                new NavItem(log.Key, log.Title, log),
                new NavItem(alerts.Key, alerts.Title, alerts),
                new NavItem(settings.Key, settings.Title, settings),
                new NavItem(about.Key, about.Title, about),
            };

            // 仪表盘「查看全部记录」跳转日志页（DESIGN 第三节 2）
            var logItem = NavItems.Single(i => i.Key == log.Key);
            dashboard.NavigateToLogRequested += (_, _) => _ = ApplyAsync(logItem);
        }

        /// <summary>全局界面状态（顶 / 底栏与仪表盘共用同一实例）。</summary>
        public IRuntimeStatusService Status { get; }

        public IReadOnlyList<NavItem> NavItems { get; }

        /// <summary>导航选中项。由视图回传（经未保存确认）或 Shell 主动切换。</summary>
        [ObservableProperty]
        private NavItem? _selectedItem;

        /// <summary>上次停留页标识，供工具条 / 托盘恢复主窗口时回到原页（DESIGN 第三节 2、第九节 4）。</summary>
        public string? LastPageKey { get; private set; }

        /// <summary>内容区数据对象（随选中页变化，视图经 DataTemplate 映射）。</summary>
        public PageViewModelBase? CurrentPage => SelectedItem?.Page;

        public string Title => "网络连通性监测工具";

        /// <summary>窗口标题：附带当前页与状态，便于多开时区分。</summary>
        public string WindowTitle => $"{Title} - {SelectedItem?.Title ?? CurrentPage?.Title ?? string.Empty}";

        /// <summary>启动时定位首页：优先上次停留页，无记录回仪表盘。</summary>
        public async Task InitializeAsync()
        {
            var target = FindItem(LastPageKey) ?? FindItem(DefaultPageKey);
            if (target is not null)
            {
                await ApplyAsync(target);
            }
        }

        /// <summary>
        /// 视图侧选中导航项时调用：无未保存修改则直接切换并返回 true；
        /// 返回 false 表示需由视图回退选中项，随后经 <see cref="ConfirmNavigateAsync"/> 处理。
        /// </summary>
        public bool TryNavigateDirect(NavItem? item)
        {
            if (item is null)
            {
                return false;
            }

            if (ReferenceEquals(item, SelectedItem))
            {
                return true;
            }

            if (CurrentPage is { IsDirty: true })
            {
                return false;
            }

            _ = ApplyAsync(item);
            return true;
        }

        /// <summary>存在未保存修改时的切页确认：保存并离开 / 放弃修改 / 留在本页。</summary>
        public async Task ConfirmNavigateAsync(NavItem? item)
        {
            if (item is null || ReferenceEquals(item, SelectedItem))
            {
                return;
            }

            if (await LeaveCurrentAsync())
            {
                await ApplyAsync(item);
            }
        }

        /// <summary>
        /// 关闭主窗口前的确认（DESIGN 第四节 4）。返回 false 表示用户取消关闭。
        /// 关闭行为（退出 / 托盘 / 工具条）本身在 S9 接入，此处只做未保存拦截。
        /// </summary>
        public async Task<bool> ConfirmCloseAsync() => await LeaveCurrentAsync();

        /// <summary>工具条 / 托盘恢复主窗口时回到上次停留页（S8、S9 调用）。</summary>
        public Task RestoreLastPageAsync()
            => ApplyAsync(FindItem(LastPageKey) ?? FindItem(DefaultPageKey)
                ?? throw new InvalidOperationException("导航项未注册"));

        private async Task<bool> LeaveCurrentAsync()
        {
            var page = CurrentPage;
            if (page is null || !page.IsDirty)
            {
                return true;
            }

            return await page.ConfirmLeaveAsync() != LeaveChoice.Cancel;
        }

        private async Task ApplyAsync(NavItem item)
        {
            SelectedItem = item;
            await item.Page.ActivateAsync();
        }

        /// <summary>选中页变化时联动内容区对象、窗口标题与停留页记忆。</summary>
        partial void OnSelectedItemChanged(NavItem? value)
        {
            if (value is not null)
            {
                LastPageKey = value.Key;
            }

            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(WindowTitle));
        }

        private NavItem? FindItem(string? key)
            => key is null ? null : NavItems.FirstOrDefault(i => i.Key == key);
    }
}
