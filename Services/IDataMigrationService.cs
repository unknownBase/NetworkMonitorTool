namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 数据目录迁移（DESIGN 第十一节 1、DEVELOPMENT_PLAN S3）。
    /// 迁移成功前继续使用旧目录；失败保持原路径、不丢数据并给出原因。
    /// </summary>
    public interface IDataMigrationService
    {
        /// <summary>
        /// 把当前数据目录中的数据库（含 -wal / -shm）与日志复制到 <paramref name="targetDirectory"/>，
        /// 验证成功后重写配置并切换 <see cref="AppPaths.Root"/>；
        /// 任一步失败则回退旧目录、不做删除，返回失败原因。
        /// </summary>
        /// <param name="targetDirectory">目标数据目录；null / 空白表示默认目录。</param>
        Task<MigrationResult> MigrateAsync(
            string? targetDirectory, CancellationToken cancellationToken = default);

        /// <summary>估算当前数据目录中待迁移数据的体积（字节），用于空间校验。</summary>
        long GetUsedBytes();
    }

    /// <summary>数据目录迁移结果。</summary>
    public sealed record MigrationResult
    {
        private MigrationResult(bool success, string from, string to, string? message, int copiedFiles)
        {
            Success = success;
            FromDirectory = from;
            ToDirectory = to;
            Message = message;
            CopiedFiles = copiedFiles;
        }

        /// <summary>是否迁移成功（成功时数据目录已切换）。</summary>
        public bool Success { get; }

        /// <summary>迁移前目录（失败时仍为当前生效目录）。</summary>
        public string FromDirectory { get; }

        /// <summary>目标目录。</summary>
        public string ToDirectory { get; }

        /// <summary>失败原因或成功摘要，供界面提示。</summary>
        public string? Message { get; }

        /// <summary>成功复制的文件数。</summary>
        public int CopiedFiles { get; }

        public static MigrationResult Ok(string from, string to, int copiedFiles, string? message = null)
            => new(true, from, to, message, copiedFiles);

        public static MigrationResult Failed(string from, string to, string reason)
            => new(false, from, to, reason, 0);

        /// <summary>目录未变化，无需迁移（视为成功，不复制不切换）。</summary>
        public static MigrationResult NotNeeded(string directory)
            => new(true, directory, directory, "目标目录与当前数据目录相同，无需迁移", 0);
    }
}
