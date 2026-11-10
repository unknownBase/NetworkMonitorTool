using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using NetworkMonitorTool.ViewModels;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 托盘图标服务（DESIGN 第三节 3.4）：应用运行期间常驻通知区，
    /// 双击恢复主窗口，右键菜单提供显示主窗口 / 转为工具条 / 检测操作 / 退出；
    /// 告警经托盘气泡通知送达（是否发送由告警设置决定，见 <see cref="AlertNotificationService"/>）。
    /// 基于 H.NotifyIcon.Wpf（纯 WPF 实现，不引用 WinForms）。
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        private readonly TaskbarIcon _icon;
        private readonly TrayViewModel _viewModel;
        private readonly IWindowModeService _modes;
        private readonly ILogger<TrayIconService>? _logger;

        public TrayIconService(
            TrayViewModel viewModel,
            IWindowModeService modes,
            ILogger<TrayIconService>? logger = null)
        {
            _viewModel = viewModel;
            _modes = modes;
            _logger = logger;

            _icon = new TaskbarIcon
            {
                IconSource = LoadIconSource(),
                ToolTipText = viewModel.ToolTipText,
                DoubleClickCommand = viewModel.ShowMainWindowCommand,
                ContextMenu = BuildMenu(viewModel),
            };

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _icon.TrayBalloonTipClicked += OnBalloonTipClicked;

            CreateIcon();
        }

        /// <summary>弹出一条告警气泡通知（点击气泡恢复主窗口）。</summary>
        public void ShowAlert(string title, string message)
        {
            try
            {
                _icon.ShowNotification(title, message, NotificationIcon.Warning);
            }
            catch (Exception ex)
            {
                // 通知渠道失败不影响检测与弹窗通道
                _logger?.LogWarning(ex, "托盘告警通知发送失败");
            }
        }

        public void Dispose()
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _icon.TrayBalloonTipClicked -= OnBalloonTipClicked;
            _icon.Dispose();
        }

        private void CreateIcon()
        {
            try
            {
                // 图标由代码创建（不在可视树中），需显式创建；不开 Efficiency Mode
                _icon.ForceCreate(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "托盘图标创建失败，其余功能不受影响");
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TrayViewModel.ToolTipText))
            {
                _icon.ToolTipText = _viewModel.ToolTipText;
            }
        }

        private void OnBalloonTipClicked(object sender, System.Windows.RoutedEventArgs e)
            => _ = _modes.ShowMainWindowAsync();

        private static ContextMenu BuildMenu(TrayViewModel viewModel)
        {
            var menu = new ContextMenu();

            menu.Items.Add(new MenuItem { Header = "显示主窗口", Command = viewModel.ShowMainWindowCommand });
            menu.Items.Add(new MenuItem { Header = "转为工具条", Command = viewModel.ShowToolbarCommand });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "启动检测", Command = viewModel.StartCommand });
            menu.Items.Add(new MenuItem { Header = "停止检测", Command = viewModel.StopCommand });
            menu.Items.Add(new MenuItem { Header = "暂停检测", Command = viewModel.PauseCommand });
            menu.Items.Add(new MenuItem { Header = "恢复检测", Command = viewModel.ResumeCommand });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "退出", Command = viewModel.ExitCommand });

            return menu;
        }

        private static BitmapImage LoadIconSource()
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
