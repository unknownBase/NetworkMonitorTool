using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using NetworkMonitorTool.Services;
using NetworkMonitorTool.ViewModels;

namespace NetworkMonitorTool.Views
{
    /// <summary>
    /// 桌面工具条窗口（DESIGN 第九节）：空白处按住拖动、双击恢复主窗口、右键菜单。
    /// 坐标与置顶由 <see cref="IWindowModeService"/> 管理。
    /// </summary>
    public partial class ToolbarWindow : Window
    {
        private readonly IWindowModeService _modes;

        public ToolbarWindow(IWindowModeService modes, ToolbarViewModel viewModel)
        {
            InitializeComponent();
            _modes = modes;
            DataContext = viewModel;
        }

        private void OnRootMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                _ = _modes.ShowMainWindowAsync();
                return;
            }

            DragMove();
        }

        /// <summary>
        /// S8 尚无托盘：Alt+F4 等关闭途径回主窗口，避免主窗口隐藏时程序不可见地运行；
        /// 明确退出走右键菜单（统一 Shutdown）。退出进行中则直接放行。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_modes.IsExiting)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            _ = _modes.ShowMainWindowAsync();
        }
    }
}