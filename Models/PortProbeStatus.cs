namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 端口检测结果三态（见 DESIGN 第七节）：界面与导出均需体现。
    /// </summary>
    public enum PortProbeStatus
    {
        /// <summary>未检测（端口未配置或按策略跳过）</summary>
        NotTested,

        /// <summary>成功</summary>
        Success,

        /// <summary>失败</summary>
        Failed,
    }
}
