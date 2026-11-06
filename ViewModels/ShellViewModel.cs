using CommunityToolkit.Mvvm.ComponentModel;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 主窗口壳视图模型（占位）：
    /// S4 迭代补全导航命令、当前页、全局状态与上次停留页记忆（见 DESIGN 第三、四节）。
    /// </summary>
    public partial class ShellViewModel : ObservableObject
    {
        /// <summary>应用标题（骨架阶段展示用）</summary>
        [ObservableProperty]
        private string _title = "网络连通性监测工具";
    }
}
