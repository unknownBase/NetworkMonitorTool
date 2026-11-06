using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetworkMonitorTool.Services;
using NetworkMonitorTool.ViewModels;
using NetworkMonitorTool.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool
{
    /// <summary>
    /// 应用入口：接入泛型主机（Host.CreateDefaultBuilder），
    /// 负责 DI 注册、日志、全局异常处理，并经主机启动主窗口。
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // 全局异常处理须在主机构建前挂接（见 DESIGN 第十四节第 7 条）
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            _host = Host.CreateDefaultBuilder(e.Args)
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddProvider(
                        new FileLoggerProvider(AppPaths.EnsureLogDirectory(), "app"));
                })
                .ConfigureServices((_, services) =>
                {
                    // ---- 视图模型 ----
                    services.AddTransient<ShellViewModel>();

                    // ---- 窗口 ----
                    services.AddSingleton<MainWindow>();

                    // ---- S2 检测核心（探测 / 编排 / 调度）----
                    services.AddDetectionCore();

                    // ---- 服务占位（S3~S10 逐个实现并注册）----
                    // services.AddSingleton<IStoreService, SqliteStoreService>();       // S3
                    // services.AddSingleton<IConfigService, JsonConfigService>();       // S3
                    // services.AddSingleton<ICleanupService, CleanupService>();         // S3
                    // services.AddSingleton<IExportService, CsvExportService>();        // S5
                    // services.AddSingleton<IAlertService, AlertService>();             // S6
                    // services.AddSingleton<IWindowModeService, WindowModeService>();   // S8
                    // services.AddSingleton<ISingleInstanceService, SingleInstanceService>(); // S10
                    // services.AddSingleton<IAutoStartService, AutoStartService>();     // S10
                })
                .Build();

            await _host.StartAsync();

            // 主窗口经主机 DI 解析启动
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = _host.Services.GetRequiredService<ShellViewModel>();
            MainWindow = mainWindow;
            mainWindow.Show();

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host is not null)
            {
                using (_host)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                }
            }

            base.OnExit(e);
        }

        /// <summary>UI 线程未捕获异常：写日志并提示，尽量保持程序存活</summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogAndNotify(e.Exception, "UI 线程未捕获异常");
            e.Handled = true;
        }

        /// <summary>后台 Task 未观察异常：写日志并提示</summary>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogAndNotify(e.Exception, "后台任务未观察异常");
            e.SetObserved();
        }

        /// <summary>运行时未捕获异常（多为无法恢复），写日志并提示后由 CLR 终止</summary>
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogAndNotify(e.ExceptionObject as Exception, "运行时未捕获异常");
        }

        /// <summary>统一异常落盘 + 弹窗提示（见 DESIGN 第十四节第 7 条）</summary>
        private void LogAndNotify(Exception? exception, string category)
        {
            var logger = _host?.Services.GetService<ILoggerFactory>()?
                .CreateLogger("GlobalException");
            logger?.LogCritical(exception, "{Category}", category);

            // 保底直写错误日志，防止日志基础设施不可用时丢失现场
            try
            {
                AppPaths.EnsureLogDirectory();
                File.AppendAllText(
                    AppPaths.ErrorLogFile,
                    $"[{DateTime.Now:O}] {category}: {exception}{Environment.NewLine}");
            }
            catch
            {
                // 写日志失败不再抛出，避免掩盖原始异常
            }

            MessageBox.Show(
                $"发生未处理的异常，详情已写入错误日志：\n{AppPaths.ErrorLogFile}\n\n{exception?.Message}",
                "网络连通性监测工具 - 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
