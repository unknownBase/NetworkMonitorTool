using System.Windows;
using Microsoft.Win32;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 基于 WPF MessageBox 与 <see cref="OpenFolderDialog"/> 的对话框实现（.NET 8 内置文件夹选择）。
    /// 仅在界面线程调用（ViewModel 命令均在界面线程触发）。
    /// </summary>
    public sealed class WpfDialogService : IDialogService
    {
        public void ShowInfo(string message, string title = "网络连通性监测工具")
            => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void ShowError(string message, string title = "网络连通性监测工具")
            => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public bool Confirm(string message, string title = "网络连通性监测工具")
            => Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        /// <summary>
        /// 默认按钮为「取消」，避免误触回车直接丢弃修改（DESIGN 第四节 4）。
        /// </summary>
        public LeaveChoice ConfirmUnsavedChanges(string message, string title = "未保存的配置修改")
            => Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) switch
            {
                MessageBoxResult.Yes => LeaveChoice.Save,
                MessageBoxResult.No => LeaveChoice.Discard,
                _ => LeaveChoice.Cancel,
            };

        public string? PickFolder(string? initialDirectory = null)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择数据目录",
                Multiselect = false,
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            var confirmed = Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            return confirmed == true ? dialog.FolderName : null;
        }

        private static MessageBoxResult Show(
            string message, string title, MessageBoxButton buttons, MessageBoxImage image,
            MessageBoxResult defaultResult = MessageBoxResult.OK)
            => Owner is { } owner
                ? MessageBox.Show(owner, message, title, buttons, image, defaultResult)
                : MessageBox.Show(message, title, buttons, image, defaultResult);

        /// <summary>以主窗口为属主；主窗口尚未创建时退化为无属主弹窗。</summary>
        private static Window? Owner => Application.Current is { MainWindow: not null } app
            ? app.MainWindow
            : null;
    }
}
