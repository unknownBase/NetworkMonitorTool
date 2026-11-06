namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// f) 启动设置（DESIGN 第四节 4、第十三节 2）。
    /// 注册表自启项的写入 / 移除由 S10 <c>IAutoStartService</c> 落地，S4 先提供设置项与持久化。
    /// </summary>
    public sealed class StartupConfig
    {
        /// <summary>是否开机自启（写入 HKCU Run 项）。</summary>
        public bool AutoStartEnabled { get; set; }

        /// <summary>自启后是否直接进入工具条形态（DESIGN 第十三节 2，默认是）。</summary>
        public bool AutoStartInToolbar { get; set; } = true;

        public StartupConfig Clone() => new()
        {
            AutoStartEnabled = AutoStartEnabled,
            AutoStartInToolbar = AutoStartInToolbar,
        };
    }
}
