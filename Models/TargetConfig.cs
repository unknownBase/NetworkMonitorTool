using System.Text.Json.Serialization;

namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 监测目标（DESIGN 第十七节、DESIGN_S16_MULTITARGET 第三节）：
    /// 名称（可空）＋ IP / 主机名 ＋ 可选端口 ＋ 启用开关。
    /// 检测参数（间隔 / 超时 / 端口策略）在 S16a 仍为全局共享，逐目标覆盖留待 S16b。
    /// </summary>
    public sealed class TargetConfig
    {
        /// <summary>
        /// v1 单目标配置迁移后使用的固定 Id，与数据库存量记录回填的 target_id 保持一致。
        /// 新建目标使用 Guid（N 格式），不会与此值冲突。
        /// </summary>
        public const string LegacyTargetId = "default";

        /// <summary>目标标识：新建为 Guid，v1 迁移目标固定为 <see cref="LegacyTargetId"/>。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>显示名，可为空；为空时界面以 <see cref="Address"/> 显示。</summary>
        public string? Name { get; set; }

        /// <summary>目标 IP 或主机名。</summary>
        public string TargetIp { get; set; } = "127.0.0.1";

        /// <summary>目标端口，null 表示不检测端口。</summary>
        public int? Port { get; set; }

        /// <summary>是否参与调度（禁用的目标保留历史，可随时重新启用）。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>地址展示：IP[:端口]。</summary>
        [JsonIgnore]
        public string Address => Port is { } port ? $"{TargetIp}:{port}" : TargetIp;

        /// <summary>界面展示名：名称非空取名称，否则取地址。</summary>
        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name.Trim();

        /// <summary>派生检测核心参数（目标 + 全局超时与端口策略）。</summary>
        public DetectionOptions ToDetectionOptions(int timeoutMs, PortDetectionPolicy portPolicy) => new()
        {
            TargetId = Id,
            TargetIp = TargetIp,
            Port = Port,
            TimeoutMs = timeoutMs,
            PortPolicy = portPolicy,
        };

        /// <summary>深拷贝（设置页草稿与配置快照隔离）。</summary>
        public TargetConfig Clone() => new()
        {
            Id = Id,
            Name = Name,
            TargetIp = TargetIp,
            Port = Port,
            Enabled = Enabled,
        };
    }
}
