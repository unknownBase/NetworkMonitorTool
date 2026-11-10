using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

        /// <summary>代码内回退选中项时抑制 SelectionChanged 递归处理。</summary>
        private bool _suppressSelection;

        /// <summary>未保存确认已通过，允许真正关闭。</summary>
        private bool _closeVerified;

        public MainWindow(IWindowModeService modes)
        {
            InitializeComponent();
            _modes = modes;
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
        /// 关闭前拦截未保存修改（DESIGN 第四节 4）。
        /// 关闭行为本身（退出 / 托盘 / 工具条，DESIGN 第三节 3）在 S9 接入窗口形态服务。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_closeVerified || _modes.IsExiting)
            {
                base.OnClosing(e);
                return;
            }

            // WPF 不允许在关闭序列中重入 Close()：先取消本轮，确认完成后再另起一轮
            e.Cancel = true;
            _ = ConfirmAndCloseAsync();
        }

        private async Task ConfirmAndCloseAsync()
        {
            if (Shell is not null && !await Shell.ConfirmCloseAsync())
            {
                return;
            }

            _closeVerified = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
