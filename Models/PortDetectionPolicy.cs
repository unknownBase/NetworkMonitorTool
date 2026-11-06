namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 端口检测策略（见 DESIGN 第七节四分支）。
    /// 策略设置 UI 在 S10 补全，此处先实现判定逻辑。
    /// </summary>
    public enum PortDetectionPolicy
    {
        /// <summary>始终检测：无论 Ping 结果都检测端口</summary>
        Always,

        /// <summary>仅 Ping 成功时检测：Ping 失败则端口记为“未检测”</summary>
        OnlyWhenPingSuccess,

        /// <summary>仅 Ping 失败时检测：用于判断主机不通还是 ICMP 被禁</summary>
        OnlyWhenPingFailed,

        /// <summary>不检测：即使配置端口也不检测</summary>
        Never,
    }
}
