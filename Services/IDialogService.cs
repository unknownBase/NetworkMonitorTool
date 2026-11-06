namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 离开有未保存修改的页面时的用户选择（DESIGN 第四节 4：切换页面或关闭窗口需提示确认）。
    /// </summary>
    public enum LeaveChoice
    {
        /// <summary>保存后继续离开。</summary>
        Save,

        /// <summary>放弃修改继续离开。</summary>
        Discard,

        /// <summary>取消离开，留在本页。</summary>
        Cancel,
    }

    /// <summary>
    /// 界面交互出口：确认框 / 提示框 / 目录选择。
    /// ViewModel 经此抽象触达对话框，便于保持 MVVM 可测试性（DESIGN 第二节 2）。
    /// </summary>
    public interface IDialogService
    {
        /// <summary>提示性信息（单按钮确认）。</summary>
        void ShowInfo(string message, string title = "网络连通性监测工具");

        /// <summary>操作失败提示（含原因），不中断流程。</summary>
        void ShowError(string message, string title = "网络连通性监测工具");

        /// <summary>是 / 否 确认。</summary>
        bool Confirm(string message, string title = "网络连通性监测工具");

        /// <summary>
        /// 未保存修改三选一确认：是 = 保存并离开，否 = 放弃修改，取消 = 留在本页。
        /// </summary>
        LeaveChoice ConfirmUnsavedChanges(string message, string title = "未保存的配置修改");

        /// <summary>
        /// 选择文件夹；返回所选绝对路径，用户取消时返回 null。
        /// </summary>
        string? PickFolder(string? initialDirectory = null);
    }
}
