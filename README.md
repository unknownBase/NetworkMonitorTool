# NetworkMonitorTool — 网络连通性监测小工具

基于 **.NET 8 + WPF** 的桌面网络连通性监测工具。周期性检测目标 IP 的连通性（可选端口检测），
提供状态标识、历史查询、CSV 导出、连续失败告警、告警历史、桌面工具条、开机自启与单实例运行。

> 完整结构设计见 [docs/DESIGN.md](docs/DESIGN.md)，版本规划见 [ROADMAP.md](ROADMAP.md)，
> 迭代推进计划见 [docs/DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md)。

---

## 1. 功能特性

| 模块 | 说明 |
| --- | --- |
| 仪表盘 | 当前状态大标识、目标摘要、最近检测数据、快捷操作、最近记录简表 |
| 检测日志 | 完整记录表格，按状态 / 时间范围筛选、排序、虚拟化、CSV 导出、清空 |
| 告警历史 | 连续失败告警记录独立成页，支持筛选与清空 |
| 设置 | 目标 / 检测 / 告警 / 存储 / 界面 / 启动六组配置，保存后生效 + 行内校验 |
| 桌面工具条 | 独立无边框小窗，可拖动、可置顶、位置记忆、双击恢复主窗口 |
| 系统托盘 | 托盘图标常驻，菜单控制显示 / 工具条 / 暂停恢复 / 退出，告警通知 |
| 连续失败告警 | 阈值触发、去重、恢复重置、冷启动计数延续 |
| 端口检测策略 | 始终检测 / 仅 Ping 成功 / 仅 Ping 失败 / 不检测，结果三态 |
| 持久化与清理 | SQLite 分表存储，按保留天数自动清理 |
| 单实例与自启 | Mutex 单实例互斥激活；开机自启注册表开关 |

## 2. 技术栈

- **框架**：.NET 8，WPF（`net8.0-windows`）
- **架构**：MVVM + 依赖注入 + 后台调度，单窗口多页导航
- **UI 库**：[WPF-UI](https://github.com/lepoco/wpfui)（Fluent 控件、Mica 背衬）
- **托盘**：[H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon)（纯 WPF 实现，不依赖 WinForms）
- **MVVM**：CommunityToolkit.Mvvm（源生成器 `[ObservableProperty]` / `[RelayCommand]`）
- **宿主 / DI**：Microsoft.Extensions.Hosting 泛型主机（自带 DI、配置、日志、BackgroundService）
- **存储**：SQLite（Microsoft.Data.Sqlite），检测记录与告警记录分表
- **配置**：`appsettings.json` 存于用户目录（`%LOCALAPPDATA%\NetworkMonitorTool`），带版本号兼容迁移

## 3. 项目结构（目标形态）

```text
NetworkMonitorTool/
├── App.xaml / App.xaml.cs        # 泛型主机启动、DI 注册、全局异常
├── Models/                       # 监测配置、检测记录、告警记录、状态枚举
├── Services/                     # 检测、调度、持久化、清理、导出、告警、
│                                 # 窗口形态、单实例、自启动
├── ViewModels/                   # 壳 VM + 各页 VM + 工具条 VM
├── Views/                        # 主窗口、各导航页、工具条窗口、告警弹窗
├── Converters/                   # 状态 → 颜色转换器
└── docs/                         # DESIGN / DEVELOPMENT_PLAN 等文档
```

## 4. 快速开始

### 环境要求

- Windows 10 1809+ / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 构建与运行

```powershell
dotnet build .\NetworkMonitorTool.csproj -c Release
dotnet run --project .\NetworkMonitorTool.csproj
```

### 数据与日志位置

| 内容 | 位置 |
| --- | --- |
| 配置文件 | `%LOCALAPPDATA%\NetworkMonitorTool\appsettings.json` |
| 数据库 | `%LOCALAPPDATA%\NetworkMonitorTool\data.db` |
| 错误日志 | `%LOCALAPPDATA%\NetworkMonitorTool\logs\` |

## 5. 使用说明摘要

1. 在 **设置页** 配置目标 IP、（可选）端口与检测间隔，点击 **保存**。
2. 在 **仪表盘页** 点击 **启动**，开始周期检测；状态标识实时更新。
3. 连续失败达到阈值时触发告警（弹窗 / 托盘通知），记录进入 **告警历史页**。
4. 关闭主窗口默认 **转为桌面工具条**，可拖动、双击恢复主窗口。
5. **日志页** 可按状态 / 时间筛选并导出 CSV；清空需二次确认。

## 6. 协作规范

提交代码请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)（单行 Conventional Commits：`type: 中文简述`）。
用户可见变更记录维护在 [CHANGELOG.md](CHANGELOG.md)。

## 7. 许可

[MIT License](LICENSE)
