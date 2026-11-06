# Changelog

本项目所有重要变更均记录于此，格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added

- 初始化文档体系：README、ROADMAP、开发推进计划（docs/DEVELOPMENT_PLAN.md）、
  总体设计说明（docs/DESIGN.md）、贡献指南与 MIT 许可。
- S1 工程骨架：`Models/ Services/ ViewModels/ Views/ Converters/` 分包（主窗口迁移至 `Views/`），
  App 接入泛型主机（`Host.CreateDefaultBuilder`）并经 DI 解析启动主窗口，
  全局异常处理（UI 线程 / 后台 Task / AppDomain）写文件日志并弹窗提示，
  引入 `Microsoft.Extensions.Hosting` 8.0.1、`CommunityToolkit.Mvvm` 8.4.2、
  `WPF-UI` 4.3.0、`Microsoft.Data.Sqlite` 8.0.21，程序集版本号 0.1.0。
- `AppPaths` 数据目录改为可配置项（`storage.dataDirectory`，默认 `%LOCALAPPDATA%\NetworkMonitorTool`），
  配置文件固定存于默认目录；对应开发计划：S3 接入 `IConfigService` 与 `IDataMigrationService`
  （保存切换目录后自动迁移已有数据，失败不丢数据），S4 设置页提供目录选择，S5 补生效联动。
- S2 检测核心：`DetectionStatus` / 端口三态 / 端口策略枚举与 `DetectionRecord` 等领域模型，
  `DetectionStatusToBrushConverter` 状态→颜色转换器，`PingProbe` 与 `TcpPortProbe`（错误摘要区分超时/不可达/权限/拒绝），
  `IDetectionService` 单次检测编排（端口策略四分支）与 `ISchedulerService` 周期调度（启动/停止/暂停/恢复，与页面解耦），
  经 `AddDetectionCore` 统一注册；新增控制台验证驱动 `tools/DetectorConsole`。

<!-- 下一版本条目在此之后追加 -->
