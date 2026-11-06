using NetworkMonitorTool.Services;
using NetworkMonitorTool.Services.Probes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetworkMonitorTool
{
    /// <summary>
    /// 检测核心与存储服务的 DI 注册。主程序与验证用控制台共用，保证行为一致。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 Ping / 端口探测、检测编排与调度服务。
        /// 调度服务依赖 <see cref="IStoreService"/> 落库，故此处一并注册存储与配置（S3）。
        /// </summary>
        public static IServiceCollection AddDetectionCore(this IServiceCollection services)
        {
            services.AddStorageCore();

            services.TryAddSingleton<IPingProbe, PingProbe>();
            services.TryAddSingleton<ITcpPortProbe, TcpPortProbe>();
            services.TryAddSingleton<IDetectionService, DetectionService>();
            services.TryAddSingleton<ISchedulerService, SchedulerService>();
            return services;
        }

        /// <summary>
        /// 注册配置读写、SQLite 持久化、数据目录迁移与过期清理（S3）。
        /// 清理以后台服务形式在主机启动后执行（启动一次 + 每小时）。
        /// </summary>
        public static IServiceCollection AddStorageCore(this IServiceCollection services)
        {
            services.TryAddSingleton<IConfigService, JsonConfigService>();
            services.TryAddSingleton<IStoreService, SqliteStoreService>();
            services.TryAddSingleton<IDataMigrationService, DataMigrationService>();
            services.TryAddSingleton<ICleanupService, CleanupService>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<
                Microsoft.Extensions.Hosting.IHostedService, CleanupHostedService>());
            return services;
        }
    }
}
