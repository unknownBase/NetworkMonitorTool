using CommunityToolkit.Mvvm.ComponentModel;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 主窗口导航页视图模型基类（DESIGN 第三、四节）。
    /// 每页以单例形式存活，切换页面不丢失页内状态与未保存草稿。
    /// </summary>
    public abstract partial class PageViewModelBase : ObservableObject
    {
        /// <summary>页面稳定标识（用于导航项与上次停留页记忆）。</summary>
        public abstract string Key { get; }

        /// <summary>导航与页头显示标题。</summary>
        public abstract string Title { get; }

        /// <summary>
        /// 是否存在未保存修改。为 true 时切换页面 / 关闭窗口需确认（DESIGN 第四节 4）。
        /// </summary>
        public virtual bool IsDirty => false;

        /// <summary>
        /// 页面被切到前台时调用（首次进入或再次返回），用于按需刷新数据。
        /// </summary>
        public virtual Task ActivateAsync() => Task.CompletedTask;

        /// <summary>
        /// 离开本页前询问用户如何处理未保存修改。
        /// 返回 <see cref="LeaveChoice.Cancel"/> 表示留在本页（保存失败也归为取消）。
        /// </summary>
        public virtual Task<LeaveChoice> ConfirmLeaveAsync() => Task.FromResult(LeaveChoice.Discard);

        /// <summary>放弃未保存修改，回到已生效配置。</summary>
        public virtual void DiscardChanges()
        {
        }
    }

    /// <summary>导航项：页面标识、标题与对应视图模型（内容区经 DataTemplate 映射到视图）。</summary>
    public sealed record NavItem(string Key, string Title, PageViewModelBase Page);
}
