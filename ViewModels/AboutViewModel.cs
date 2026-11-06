using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 关于页视图模型（DESIGN 第四节 5）：版本号、配置文件 / 数据库 / 日志位置与使用说明入口。
    /// 路径均取当前生效数据目录，数据目录切换后立即反映新位置。
    /// </summary>
    public sealed partial class AboutViewModel : PageViewModelBase
    {
        private readonly IConfigService _config;
        private readonly IStoreService _store;
        private readonly IDialogService _dialog;

        public AboutViewModel(IConfigService config, IStoreService store, IDialogService dialog)
        {
            _config = config;
            _store = store;
            _dialog = dialog;
        }

        public override string Key => "about";

        public override string Title => "关于";

        public string ProductName => "网络连通性监测工具";

        /// <summary>程序集版本（AssemblyInfo 中维护，对应 ROADMAP 里程碑）。</summary>
        public string VersionText =>
            typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "未知版本";

        public string ConfigPath => _config.ConfigFilePath;

        public string DatabasePath => AppPaths.DatabaseFile;

        public string LogDirectoryPath => AppPaths.LogDirectory;

        public string ErrorLogPath => AppPaths.ErrorLogFile;

        /// <summary>数据目录是否使用了自定义值（关于页需说明实际生效位置）。</summary>
        public string DataDirectoryState =>
            string.IsNullOrWhiteSpace(_config.Current.Storage.DataDirectory)
                ? "默认目录（未自定义）"
                : "自定义目录（配置项 storage.dataDirectory）";

        /// <summary>持久化可用性（DESIGN 第十四节 3 的界面可见入口）。</summary>
        public string PersistenceState => _store.IsPersistent
            ? "持久化正常"
            : "持久化不可用，记录仅保留在内存中";

        public string UsageGuide =>
            "1. 在「设置」页填写目标 IP（可选端口）与检测间隔，保存后生效。\n" +
            "2. 在「仪表盘」点击启动，即可周期性检测；暂停 / 恢复不打断周期。\n" +
            "3. 「检测日志」页查看保留期内的历史记录，筛选与导出 CSV 在后续版本提供。\n" +
            "4. 连续失败达到告警阈值时的弹窗 / 托盘通知在后续版本提供。";

        [RelayCommand]
        private void OpenDataDirectory() => OpenDirectory(AppPaths.Root);

        [RelayCommand]
        private void OpenLogDirectory() => OpenDirectory(AppPaths.EnsureLogDirectory());

        [RelayCommand]
        private void OpenConfigDirectory() => OpenDirectory(AppPaths.DefaultRoot);

        private void OpenDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                _dialog.ShowError($"目录尚不存在：{path}");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"无法打开目录：{path}\n{ex.Message}");
            }
        }
    }
}
