namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 检测状态（见 DESIGN 第十节状态标识）。
    /// 其中 <see cref="Paused"/> 与 <see cref="Alerting"/> 为界面状态，不写入检测日志。
    /// </summary>
    public enum DetectionStatus
    {
        /// <summary>成功（绿色）</summary>
        Success,

        /// <summary>Ping 失败（红色）</summary>
        PingFailed,

        /// <summary>端口失败（橙色）</summary>
        PortFailed,

        /// <summary>异常（深红 / 紫色）：检测过程抛出未预期错误</summary>
        Error,

        /// <summary>暂停中（灰色，界面状态）</summary>
        Paused,

        /// <summary>告警中（红色 + 醒目标记，界面状态）</summary>
        Alerting,
    }
}
