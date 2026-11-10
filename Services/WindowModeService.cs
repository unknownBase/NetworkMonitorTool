using System.IO;
using System.Text.Json;
using System.Windows;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.ViewModels;
using NetworkMonitorTool.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 窗口形态服务默认实现（DESIGN 第三节 3、第九节）：主窗口 ↔ 工具条互斥显示，
    /// 关闭主窗口按设置转工具条 / 最小化到托盘 / 退出；仅托盘形态下无可见窗口、由托盘图标操作。
    /// 工具条坐标存于数据目录 window-state.json（随目录迁移），首次显示默认主屏工作区右下角；
    /// 恢复坐标时按虚拟屏幕边界校正，防止多显示器 / 分辨率变化后越界不可见。
    /// 窗口经 <see cref="IServiceProvider"/> 懒解析，避免与视图模型的构造环依赖。
    /// </summary>
    public sealed class WindowModeService : IWindowModeService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly IServiceProvider _services;
        private readonly IConfigService _config;
        private readonly ILogger<WindowModeService>? _logger;

        private ToolbarWindow? _toolbar;

        public WindowModeService(
            IServiceProvider services,
            IConfigService config,
            ILogger<WindowModeService>? logger = null)
        {
            _services = services;
            _config = config;
            _logger = logger;

            _config.ConfigChanged += (_, _) => ApplyTopmost();
        }

        public WindowMode CurrentMode { get; private set; } = WindowMode.MainWindow;

        public bool IsExiting { get; private set; }

        public event EventHandler<WindowMode>? ModeChanged;

        public Task ShowToolbarAsync()
        {
            if (CurrentMode == WindowMode.Toolbar)
            {
                return Task.CompletedTask;
            }

            // 先置形态防重入（双击 / 连点）
            SetMode(WindowMode.Toolbar);

            if (_toolbar is null)
            {
                _toolbar = _services.GetRequiredService<ToolbarWindow>();
                PositionToolbar();
            }

            ApplyTopmost();
            var main = _services.GetRequiredService<MainWindow>();
            NormalizeIfMinimized(main);
            main.Hide();
            _toolbar.Show();
            _toolbar.Activate();
            return Task.CompletedTask;
        }

        public async Task ShowMainWindowAsync()
        {
            if (CurrentMode == WindowMode.MainWindow)
            {
                return;
            }

            SetMode(WindowMode.MainWindow);
            SavePosition();
            _toolbar?.Hide();

            await _services.GetRequiredService<ShellViewModel>().RestoreLastPageAsync();

            var main = _services.GetRequiredService<MainWindow>();
            NormalizeIfMinimized(main);
            main.Show();
            main.Activate();
        }

        public Task CloseMainWindowAsync()
        {
            return _config.Current.Interface.CloseMainWindowBehavior switch
            {
                CloseWindowBehavior.Exit => ExitAsync(),
                CloseWindowBehavior.MinimizeToTray => HideMainWindowToTrayAsync(),
                _ => ShowToolbarAsync(),
            };
        }

        public Task ExitAsync()
        {
            IsExiting = true;
            SavePosition();
            Application.Current.Shutdown();
            return Task.CompletedTask;
        }

        /// <summary>最小化到托盘：隐藏两个窗口，仅保留常驻托盘图标（DESIGN 第三节 3）。</summary>
        private Task HideMainWindowToTrayAsync()
        {
            SetMode(WindowMode.Tray);
            SavePosition();
            _toolbar?.Hide();

            var main = _services.GetRequiredService<MainWindow>();
            NormalizeIfMinimized(main);
            main.Hide();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 隐藏 / 显示前把最小化窗口还原为普通状态：否则 Hide 期间的最小化状态
        /// 会使后续 Show 仍以最小化呈现、用户看不到窗口。
        /// </summary>
        private static void NormalizeIfMinimized(Window window)
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
        }

        /// <summary>
        /// 越界校正（纯函数，供控制台回归）：将窗口左上角夹取到屏幕矩形内，
        /// 保证整窗可见；窗口比屏幕还大时贴屏幕左上角。
        /// </summary>
        public static (double Left, double Top) ClampPosition(
            double left, double top, double width, double height,
            double screenLeft, double screenTop, double screenWidth, double screenHeight)
        {
            var maxLeft = screenLeft + Math.Max(0, screenWidth - width);
            var maxTop = screenTop + Math.Max(0, screenHeight - height);
            return (Math.Clamp(left, screenLeft, maxLeft), Math.Clamp(top, screenTop, maxTop));
        }

        private static (double Left, double Top) ClampToVirtualScreen(double left, double top, double width, double height)
            => ClampPosition(
                left, top, width, height,
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        private void PositionToolbar()
        {
            var toolbar = _toolbar!;
            var state = LoadState();
            double left;
            double top;
            if (state is not null)
            {
                (left, top) = ClampToVirtualScreen(state.ToolbarLeft, state.ToolbarTop, toolbar.Width, toolbar.Height);
            }
            else
            {
                // 首次显示：主屏工作区右下角（避开任务栏），再经同一校正兜底
                var work = SystemParameters.WorkArea;
                (left, top) = ClampToVirtualScreen(
                    work.Right - toolbar.Width - 12, work.Bottom - toolbar.Height - 12,
                    toolbar.Width, toolbar.Height);
            }

            toolbar.Left = left;
            toolbar.Top = top;
        }

        private void ApplyTopmost()
        {
            if (_toolbar is not null)
            {
                _toolbar.Topmost = _config.Current.Interface.ToolbarTopMost;
            }
        }

        private void SetMode(WindowMode mode)
        {
            CurrentMode = mode;
            ModeChanged?.Invoke(this, mode);
        }

        private void SavePosition()
        {
            if (_toolbar is null)
            {
                return;
            }

            try
            {
                AppPaths.EnsureRoot();
                var json = JsonSerializer.Serialize(
                    new ToolbarState(_toolbar.Left, _toolbar.Top), JsonOptions);
                File.WriteAllText(AppPaths.WindowStateFile, json);
            }
            catch (Exception ex)
            {
                // 坐标丢失仅影响下次落点，不阻断形态切换与退出
                _logger?.LogWarning(ex, "工具条坐标保存失败：{Path}", AppPaths.WindowStateFile);
            }
        }

        private ToolbarState? LoadState()
        {
            try
            {
                return File.Exists(AppPaths.WindowStateFile)
                    ? JsonSerializer.Deserialize<ToolbarState>(File.ReadAllText(AppPaths.WindowStateFile), JsonOptions)
                    : null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "工具条坐标读取失败，按默认位置显示：{Path}", AppPaths.WindowStateFile);
                return null;
            }
        }

        private sealed record ToolbarState(double ToolbarLeft, double ToolbarTop);
    }
}