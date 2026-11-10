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
- S3 存储与配置：SQLite 分表持久化（`detect_record` / `alert_record`，WAL + 串行写入队列，
  写库失败降级为仅内存并经 `IStoreService.WriteFailed` 上报，不阻断调度），
  `MonitorConfig` / `DetectionConfig` / `AlertConfig` / `StorageConfig` 模型与 `AlertRecord` 告警记录模型（含配置版本号），
  `IConfigService`/`JsonConfigService` 固定路径 `appsettings.json` 读写（camelCase、临时文件后替换、缺失字段取默认、
  保存前校验 IP/端口/间隔/超时/阈值/保留天数/内存条数/数据目录），
  数据目录解析改由 `IConfigService` 统一接管（移除 `AppPaths` 内的临时 JSON 解析），
  `IDataMigrationService` 安全切换数据目录（数据库含 -wal/-shm 与日志复制、integrity_check 与记录数验证、
  失败清理副本并保持原目录），`ICleanupService` + `CleanupHostedService` 按保留天数启动清理与每小时定时清理；
  `DetectorConsole` 扩展 `--load-only` / `--seed` / `--clear` / `--save-config` / `--migrate` / `--data-dir` 以验证 S3 收口。
- S4 页面框架：主窗口壳（左侧五页导航 + 内容区 + 顶/底栏全局状态标识，经 `ContentControl` + `DataTemplate` 做 ViewModel-first 导航），
  `ShellViewModel` 统一处理导航命令、当前页、上次停留页记忆与未保存离页 / 离窗拦截（`LeaveChoice`：保存 / 放弃 / 取消），
  `IRuntimeStatusService`/`RuntimeStatusService` 全局界面状态中心（顶底栏、仪表盘与后续工具条 / 托盘共用一套状态含义，
  后台事件经 `Dispatcher` 编组，订阅调度状态与 `IStoreService.WriteFailed` 降级上报），
  仪表盘 / 日志 / 告警历史 / 设置 / 关于五个页面（`PageViewModelBase` 约定 `IsDirty` / `ActivateAsync` / `ConfirmLeaveAsync`）：
  仪表盘提供状态大标识、目标摘要、最近检测时间 / 延迟 / 连续失败数与启动 / 停止 / 暂停 / 恢复快捷操作及查看全部记录跳转；
  设置页按 DESIGN 六组分区（目标 / 检测 / 告警 / 存储 / 界面 / 启动）提供行内校验、基于序列化比较的未保存判定、
  保存前数据目录迁移联动与失败回退提示；关于页展示版本与配置 / 数据库 / 日志路径并可一键打开目录；
  `IDialogService`/`WpfDialogService` 统一消息提示、确认与文件夹选择对话框（.NET 8 `OpenFolderDialog`）；
  `IConfigService` 新增 `ConfigChanged` 事件；新增 `InterfaceConfig`（关闭主窗口行为、工具条置顶 / 自动转换）与
  `StartupConfig`（开机自启、自启后进工具条）两组配置与 `MonitorConfig.Clone()` 深拷贝；
  新增 `DetectionStatusToText` / `PortProbeStatusToText` / `BoolToText` / `TextToVisibility` 转换器；
  `IDataMigrationService` 修正源目录尚无数据库时的迁移基线（避免首次启动即改目录时永远切不过去）。
  本迭代仅落页面与配置项，工具条（S8）、关闭行为（S9）、开机自启（S10）的真实行为待后续交付。
- S5 日志与导出（v0.1 MVP 收口）：日志页接入状态筛选（全部 / 成功 / Ping 失败 / 端口失败 / 异常）与
  时间范围筛选（含当日，起止颠倒行内提示），表格绑定 `ICollectionView` 默认时间降序、列头可切换排序，
  加载按内存最大条数取最近 N 条（数据库仍保留完整保留期数据），筛选条数实时统计；
  `IExportService`/`CsvExportService` 导出当前筛选结果（UTF-8 带 BOM、RFC 4180 字段转义、
  默认文件名含目标 IP 与时间戳，状态文案复用界面转换器保证一致），`IDialogService` 新增 CSV 保存对话框，
  完成后提示条数与路径、失败提示原因；清空经二次确认后同清界面集合与数据库；新增打开日志目录操作；
  `DetectorConsole` 扩展 `--export` 验证 BOM / 转义 / 条数。设置保存重启调度与数据目录迁移联动沿用 S4 落地路径。
- S6 告警引擎：`IAlertService`/`AlertService` 连续失败累加、成功一次清零与「已告警」去重（恢复成功重置），
  端口失败计入开关（关闭时端口失败既不累加也不清零），阈值与计入规则保存后立即用于后续判断；
  冷启动读取最近连续失败次数延续，调度启动时已达阈值按规则立即告警；
  评估点位于调度循环检测与落库之间，计数随 `DetectionRecord.ConsecutiveFailures` 持久化（新增 `WithConsecutiveFailures` 副本方法），
  触发时写入 `alert_record` 分表（含送达通道位标志，写库失败降级不阻断）并广播 `AlertTriggered` 供 S7 弹窗 / 托盘订阅；
  暂停期间调度不产生记录故不计数不告警，停止保留计数与告警状态；
  `DetectorConsole` 扩展 `--threshold` / `--count-port`（仅本次运行）并输出告警触发与引擎状态，
  回归覆盖断网达阈恰好告警一次、冷启动延续、成功重置与计入开关两态。
- S7 告警展示：`AlertNotificationService` 订阅 `AlertTriggered` 经 `Dispatcher` 编组弹窗
  （内容含目标、连续失败次数、最近失败时间与原因摘要，受设置页弹窗开关控制；
  托盘通知开关与送达通道记录已生效，实际托盘投递随 S9 接入）；
  `AlertRecord` 新增 `LastFailureAt` 并经 `alert_record.last_failure_at` 列探测迁移持久化（旧数据为空）；
  `IRuntimeStatusService` 新增 `IsAlerting`，状态优先级暂停中 > 告警中 > 最近检测结果，
  仪表盘红色横幅与顶栏「!」醒目标记随告警触发显示、恢复成功自动复位；
  告警历史页补全：时间范围筛选（含当日、起止颠倒行内提示）、`ICollectionView` 默认时间降序与列头排序、
  虚拟化表格、导出当前筛选结果（`IExportService.ExportAlertRecordsAsync`，UTF-8 BOM、RFC 4180 转义、
  默认文件名 `告警历史_{目标}_{时间戳}.csv`）、清空二次确认同清界面与数据库、运行期新告警实时追加；
  新增 `AlertDeliveryMethodsToTextConverter`（界面与导出共用「告警方式」文案）；
  `DetectorConsole` 扩展 `--export-alerts` 验证 BOM / 转义 / 最近失败时间列与旧数据兼容。
- S8 桌面工具条：`IWindowModeService`/`WindowModeService` 主窗口 ↔ 工具条互斥切换
  （窗口懒解析避免构造环，`WindowMode` 预留托盘形态随 S9 接入），
  仪表盘「转为工具条」按钮启用；`ToolbarWindow`/`ToolbarViewModel` 无边框紧凑窗口
  （目标 IP、状态色块与文案、最近延迟、连续失败次数、启动 / 停止 / 暂停 / 恢复 / 主窗口五按钮，
  状态绑定全局状态中心含「告警中」红色标记），空白处按住拖动、双击恢复主窗口并回到上次停留页、
  右键菜单（启动停止 / 暂停恢复 / 恢复主窗口 / 退出）；
  坐标存数据目录 `window-state.json` 并纳入目录迁移清单，首次显示默认主屏工作区右下角，
  恢复时按虚拟屏幕边界夹取防越界（`ClampPosition` 纯函数）；工具条置顶随设置保存立即生效；
  右键退出保存坐标后统一 `Shutdown`（`IsExiting` 令窗口关闭拦截放行），
  S8 无托盘期间工具条 Alt+F4 回主窗口避免不可见运行；`App` 关闭模式改为 `OnMainWindowClose`；
  `DetectorConsole` 扩展 `--clamp-check` 回归越界校正 5 例。关闭行为三选项与最小化自动转换随 S9 接入。
- S9 托盘与形态切换：`TrayIconService` + `TrayViewModel` 常驻托盘图标（`H.NotifyIcon.Wpf` 2.3.2，
  因 WPF-UI 4.3.0 不含托盘控件而选用纯 WPF 实现、不引入 WinForms），右键菜单提供显示主窗口 / 转为工具条 /
  启动停止 / 暂停恢复 / 退出，双击与气泡点击恢复主窗口，悬浮提示随状态、目标与连续失败次数刷新；
  新增 `Assets/app.ico`（256 PNG + 64/48/32/16 DIB 多尺寸、圆角外透明）同时作为 exe 文件图标与托盘图标；
  `IWindowModeService` 新增 `CloseMainWindowAsync`：主窗口关闭先确认未保存修改，再按设置分派
  退出 / 最小化到托盘 / 转为工具条（默认，读取当前配置故保存后立即生效）；主窗口最小化时按开关
  自动转工具条（隐藏前还原最小化状态）；退出三入口（托盘菜单 / 工具条右键 / 关闭行为）统一
  `ExitAsync` → `Shutdown`；`AlertNotificationService` 接入托盘气泡通知（受 `TrayNotifyEnabled`
  实时开关），补齐 S7 遗留通道；设置页生效说明同步更新。

<!-- 下一版本条目在此之后追加 -->
