using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;
using NetworkMonitorTool.ViewModels;

namespace NetworkMonitorTool.Views
{
    /// <summary>
    /// 主窗口壳（DESIGN 第三节 1）。导航选中项由壳层校验后才生效：
    /// 存在未保存修改时先回退选中项，再由壳层弹出「保存 / 放弃 / 取消」确认。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IWindowModeService _modes;
        private readonly IConfigService _config;

        /// <summary>代码内回退选中项时抑制 SelectionChanged 递归处理。</summary>
        private bool _suppressSelection;

        public MainWindow(IWindowModeService modes, IConfigService config)
        {
            InitializeComponent();
            _modes = modes;
            _config = config;
        }

        private ShellViewModel? Shell => DataContext as ShellViewModel;

        private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection || Shell is not { } shell)
            {
                return;
            }

            if (NavList.SelectedItem is not NavItem item)
            {
                RestoreSelection(shell);
                return;
            }

            // 壳层主动切换后经 OneWay 绑定回显，无需再次导航
            if (ReferenceEquals(item, shell.SelectedItem))
            {
                return;
            }

            if (shell.TryNavigateDirect(item))
            {
                return;
            }

            // 有未保存修改：先把选中项退回原页，等用户在三选一确认里决定
            RestoreSelection(shell);
            _ = shell.ConfirmNavigateAsync(item);
        }

        private void RestoreSelection(ShellViewModel shell)
        {
            _suppressSelection = true;
            NavList.SelectedItem = shell.SelectedItem;
            _suppressSelection = false;
        }

        /// <summary>
        /// 关闭前拦截未保存修改（DESIGN 第四节 4）；确认后按关闭行为（DESIGN 第三节 3）
        /// 交给窗口形态服务：转工具条 / 最小化到托盘 / 退出。统一退出时直接放行。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_modes.IsExiting)
            {
                base.OnClosing(e);
                return;
            }

            // 本轮先取消：确认期间窗口保持打开，确认通过后由形态服务决定隐藏或退出
            e.Cancel = true;
            _ = ConfirmAndCloseAsync();
        }

        private async Task ConfirmAndCloseAsync()
        {
            if (Shell is not null && !await Shell.ConfirmCloseAsync())
            {
                return;
            }

            await _modes.CloseMainWindowAsync();
        }

        /// <summary>主窗口最小化时按设置自动转为工具条（DESIGN 第三节 3）。</summary>
        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState != WindowState.Minimized
                || _modes.IsExiting
                || _modes.CurrentMode != WindowMode.MainWindow
                || !_config.Current.Interface.ToolbarAutoOnMinimize)
            {
                return;
            }

            // 等本次状态切换结束再隐藏窗口，避免关闭 / 隐藏序列中重入
            Dispatcher.BeginInvoke(new Action(() => _ = _modes.ShowToolbarAsync()));
        }
    }
}
