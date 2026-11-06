using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 配置读写（DESIGN 第五节）。配置文件固定存于默认用户目录，
    /// 缺失字段取默认值；数据目录合法性由本服务统一校验并生效（替代 S1 的临时解析）。
    /// </summary>
    public interface IConfigService
    {
        /// <summary>配置文件固定路径（始终位于默认根目录，不随数据目录迁移）。</summary>
        string ConfigFilePath { get; }

        /// <summary>内存中的当前配置（<see cref="Load"/> 或 <see cref="SaveAsync"/> 后更新）。</summary>
        MonitorConfig Current { get; }

        /// <summary>
        /// 从磁盘读取配置：文件缺失 / 损坏 / 字段缺失均回退默认值；
        /// 同时校验并应用 <c>storage.dataDirectory</c> 到 <see cref="AppPaths.Root"/>。
        /// </summary>
        MonitorConfig Load();

        /// <summary>
        /// 写盘并更新 <see cref="Current"/>，同时把配置中的数据目录应用到 <see cref="AppPaths.Root"/>。
        /// 调用前须通过 <see cref="Validate"/>；若涉及数据目录变更，
        /// 须先经 <see cref="IDataMigrationService"/> 迁移成功，否则历史数据将不可见。
        /// </summary>
        Task<bool> SaveAsync(MonitorConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 保存前校验（DESIGN 第五节 2）：IP、端口范围、间隔大于 0、超时大于 0、
        /// 保留天数与阈值合法、数据目录可创建可写且空间充足。
        /// 返回错误列表，空列表表示通过（供设置页行内提示，S4）。
        /// </summary>
        IReadOnlyList<ConfigValidationError> Validate(MonitorConfig config);

        /// <summary>返回一份默认配置（设置页“恢复默认”）。</summary>
        MonitorConfig CreateDefault();

        /// <summary>
        /// 配置写盘生效（<see cref="SaveAsync"/> 成功）后触发，
        /// 供展示配置摘要的页面刷新（仪表盘 / 后续工具条）。
        /// </summary>
        event EventHandler<MonitorConfig>? ConfigChanged;
    }

    /// <summary>
    /// 单项配置校验错误。<see cref="Field"/> 为稳定标识（设置页据此定位控件），
    /// <see cref="Message"/> 为可直接展示的中文提示。
    /// </summary>
    public sealed record ConfigValidationError(string Field, string Message)
    {
        public override string ToString() => Message;
    }
}
