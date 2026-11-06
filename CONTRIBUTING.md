# 贡献指南

感谢参与 NetworkMonitorTool。本文约定协作方式与 Git 提交规范，动手开发前请先阅读 [docs/DESIGN.md](docs/DESIGN.md) 与 [docs/DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md)。

---

## 1. 分支与提交

- 主分支为 `main`，保持可构建、可运行。
- 小改动可直接提交到 `main`；涉及多文件或行为变更时，建议使用短生命周期分支，例如 `feat/tray-lifecycle`、`fix/scheduler-restart-on-save`。
- 一个提交只做一件事；不要把格式化、重构和功能混在同一次提交里。
- 提交前确认 `dotnet build .\NetworkMonitorTool.csproj -c Release` 通过。

## 2. Git 提交规范

采用**单行 Conventional Commits**：`type: 中文简述`，不带 body。

### 格式

```text
<type>: <中文简述>
```

规则：

- `type` 使用下方小写英文枚举。
- 冒号后必须有一个空格。
- 简述使用中文，以动词开头，直接说明做了什么。
- 结尾不加句号，不使用表情符号。
- 建议不超过 50 个汉字；需要解释原因时写进代码注释、文档或 PR 描述，不写进提交标题。
- 不使用 `Update xxx`、`修改`、`提交` 这类无信息量的标题。

### 常用 type

| type | 用途 | 示例 |
| --- | --- | --- |
| `feat` | 新功能 | `feat: 接入 Ping 与端口连通性检测` |
| `fix` | 缺陷修复 | `fix: 修正保存配置后调度周期未重启` |
| `docs` | 文档 | `docs: 补充冷启动告警延续说明` |
| `refactor` | 重构（不改变行为） | `refactor: 拆分调度服务与检测服务` |
| `chore` | 构建、依赖、杂项 | `chore: 引入 CommunityToolkit.Mvvm 8.3` |
| `style` | 格式、空白、命名（不改逻辑） | `style: 统一文件范围命名空间` |
| `test` | 测试 | `test: 增加连续失败告警去重用例` |

### 示例

```text
feat: 接入 SQLite 检测记录持久化
fix: 修正工具条多显示器坐标越界
docs: 初始化 README 与 ROADMAP
chore: 配置 net8.0-windows 目标框架
```

### 不推荐

```text
update
修改了一些东西
feat: 新增功能。
feat:新增功能
feat: 新增功能

（多行 body）
```

## 3. 提交前检查

```powershell
dotnet build .\NetworkMonitorTool.csproj -c Release
```

- [ ] 构建通过，无新增警告（或已记录豁免理由）。
- [ ] 未提交 `bin/`、`obj/`、`.vs/`、`*.user`、日志与本地数据（`%LOCALAPPDATA%\NetworkMonitorTool` 下内容不属于仓库）。
- [ ] 行为变更已补充或更新 CHANGELOG.md。
- [ ] 行为、配置项、状态定义变化已同步 docs/DESIGN.md（冲突时以设计文档为准）。
- [ ] 手测覆盖对应 P0 用例；未覆盖的部分在 PR 或提交说明中注明。
- [ ] 没有引入 WinForms 界面框架（托盘使用 WPF-UI NotifyIcon，不混用 WinForms Forms）。

## 4. 版本与发布

- 版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)；`0.x` 阶段允许接口与配置结构调整。
- 配置结构调整时须递增配置文件版本号，并保证旧配置缺失字段取默认值（兼容性规则见 DESIGN 第五节）。
- 每个里程碑结束时更新 [ROADMAP.md](ROADMAP.md) 状态与勾选项，迭代完成回填 [docs/DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md)。
- 用户可见变更记录到 [CHANGELOG.md](CHANGELOG.md)，不要只依赖提交历史。

## 5. 许可

提交代码即表示同意以本项目的 [MIT License](LICENSE) 授权。
