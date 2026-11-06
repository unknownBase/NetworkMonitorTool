using NetworkMonitorTool.Services;
using NetworkMonitorTool.Services.Probes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetworkMonitorTool
{
    /// <summary>
    /// 检测核心服务注册（S2）。主程序与验证用控制台共用，保证行为一致。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>注册 Ping / 端口探测、检测编排与调度服务。</summary>
        public static IServiceCollection AddDetectionCore(this IServiceCollection services)
        {
            services.TryAddSingleton<IPingProbe, PingProbe>();
            services.TryAddSingleton<ITcpPortProbe, TcpPortProbe>();
            services.TryAddSingleton<IDetectionService, DetectionService>();
            services.TryAddSingleton<ISchedulerService, SchedulerService>();
            return services;
        }
    }
}
