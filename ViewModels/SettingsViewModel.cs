using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMonitorTool.Models;
using NetworkMonitorTool.Services;

namespace NetworkMonitorTool.ViewModels
{
    /// <summary>
    /// 设置页视图模型（DESIGN 第四节 4 六组分区、第五节生效策略）。
    /// 编辑的是配置草稿：保存才生效，取消 / 离开时可放弃修改；
    /// 校验复用 <see cref="IConfigService.Validate"/>，错误按字段行内提示，不弹窗打断。
    /// 数据目录变更须先经 <see cref="IDataMigrationService"/> 迁移成功后才写配置（DESIGN 第十一节 1）。
    /// </summary>
    public sealed partial class SettingsViewModel : PageViewModelBase
    {
        private readonly IConfigService _config;
        private readonly IDataMigrationService _migration;
        private readonly ISchedulerService _scheduler;
        private readonly IDialogService _dialog;

        /// <summary>加载 / 放弃修改期间抑制脏标记计算。</summary>
        private bool _loading;

        public SettingsViewModel(
            IConfigService config,
            IDataMigrationService migration,
            ISchedulerService scheduler,
            IDialogService dialog)
        {
            _config = config;
            _migration = migration;
            _scheduler = scheduler;
            _dialog = dialog;

            PortPolicyOptions = new[]
            {
                new EnumOption<PortDetectionPolicy>(PortDetectionPolicy.Always, "始终检测"),
                new EnumOption<PortDetectionPolicy>(PortDetectionPolicy.OnlyWhenPingSuccess, "仅 Ping 成功时检测"),
                new EnumOption<PortDetectionPolicy>(PortDetectionPolicy.OnlyWhenPingFailed, "仅 Ping 失败时检测"),
                new EnumOption<PortDetectionPolicy>(PortDetectionPolicy.Never, "不检测"),
            };

            CloseBehaviorOptions = new[]
            {
                new EnumOption<CloseWindowBehavior>(CloseWindowBehavior.ShowToolbar, "转为工具条（默认）"),
                new EnumOption<CloseWindowBehavior>(CloseWindowBehavior.MinimizeToTray, "最小化到托盘"),
                new EnumOption<CloseWindowBehavior>(CloseWindowBehavior.Exit, "退出程序"),
            };

            // 目标行草稿的属性变化参与脏标记判定（逐行编辑不经过本类属性）
            TargetDrafts.CollectionChanged += (_, _) => RefreshDirtyState();

            LoadFrom(_config.Current);
        }

        public override string Key => "settings";

        public override string Title => "设置";

        public IReadOnlyList<EnumOption<PortDetectionPolicy>> PortPolicyOptions { get; }

        public IReadOnlyList<EnumOption<CloseWindowBehavior>> CloseBehaviorOptions { get; }

        // ---- a) 目标设置（S16 多目标列表，DESIGN_S16_MULTITARGET 第八节 3）----

        /// <summary>目标草稿集合：保存后生效，至少保留 1 个目标。</summary>
        public ObservableCollection<TargetDraft> TargetDrafts { get; } = new();

        /// <summary>目标列表级错误（如「至少需要一个监测目标」），展示在列表下方。</summary>
        [ObservableProperty] private string _targetsError = string.Empty;

        // ---- b) 检测设置 ----

        [ObservableProperty] private string _intervalHoursText = "0";
        [ObservableProperty] private string _intervalMinutesText = "1";
        [ObservableProperty] private string _intervalSecondsText = "0";
        [ObservableProperty] private string _timeoutMsText = "3000";
        [ObservableProperty] private PortDetectionPolicy _portPolicy;

        // ---- c) 告警设置 ----

        [ObservableProperty] private string _thresholdText = "3";
        [ObservableProperty] private bool _countPortFailure = true;
        [ObservableProperty] private bool _popupEnabled = true;
        [ObservableProperty] private bool _trayNotifyEnabled = true;

        // ---- d) 存储设置 ----

        [ObservableProperty] private string _retentionDaysText = "30";
        [ObservableProperty] private string _maxInMemoryText = "5000";

        /// <summary>数据目录（展示为绝对路径；留空表示默认目录）。</summary>
        [ObservableProperty] private string _dataDirectory = string.Empty;

        /// <summary>数据目录说明：默认目录与自定义目录的语义（配置文件始终留在默认目录）。</summary>
        public string DataDirectoryHint =>
            $"配置文件固定存于 {_config.ConfigFilePath}，不随数据目录迁移；留空表示使用默认目录 {AppPaths.DefaultRoot}";

        // ---- e) 界面设置 ----

        [ObservableProperty] private CloseWindowBehavior _closeMainWindowBehavior = CloseWindowBehavior.ShowToolbar;
        [ObservableProperty] private bool _toolbarTopMost = true;
        [ObservableProperty] private bool _toolbarAutoOnMinimize = true;

        // ---- f) 启动设置 ----

        [ObservableProperty] private bool _autoStartEnabled;
        [ObservableProperty] private bool _autoStartInToolbar = true;

        /// <summary>后续迭代才生效的项提示（开机自启 S10；置顶 S8、关闭行为与最小化转换 S9 已生效）。</summary>
        public string DeferredHint { get; } =
            "工具条置顶、关闭行为与最小化自动转换已生效；开机自启在 S10 生效，当前仅保存配置";

        // ---- 行内校验提示（DESIGN 第五节：不弹窗打断）----

        [ObservableProperty] private string _intervalError = string.Empty;
        [ObservableProperty] private string _timeoutError = string.Empty;
        [ObservableProperty] private string _thresholdError = string.Empty;
        [ObservableProperty] private string _retentionDaysError = string.Empty;
        [ObservableProperty] private string _maxInMemoryError = string.Empty;
        [ObservableProperty] private string _dataDirectoryError = string.Empty;

        /// <summary>保存结果 / 待处理提示，展示在页面底部。</summary>
        [ObservableProperty] private string _saveMessage = string.Empty;

        /// <summary>存在未保存修改时启用「保存 / 取消」并触发离页确认。</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _isDirty;

        public override async Task<LeaveChoice> ConfirmLeaveAsync()
        {
            if (!IsDirty)
            {
                return LeaveChoice.Discard;
            }

            var choice = _dialog.ConfirmUnsavedChanges(
                "设置页存在未保存的修改。\n\n是：保存并离开\n否：放弃本次修改\n取消：留在本页继续编辑");

            if (choice != LeaveChoice.Save)
            {
                return choice;
            }

            await SaveAsync();

            // 保存失败（校验未通过 / 迁移失败）时留在本页继续编辑
            return IsDirty ? LeaveChoice.Cancel : LeaveChoice.Save;
        }

        public override void DiscardChanges()
        {
            LoadFrom(_config.Current);
            SaveMessage = "已放弃未保存的修改";
        }

        [RelayCommand(CanExecute = nameof(CanCommitChanges))]
        private async Task SaveAsync()
        {
            if (await SaveCoreAsync())
            {
                SaveMessage = "配置已保存并生效";
            }
        }

        [RelayCommand]
        private void RestoreDefault()
        {
            LoadFrom(_config.CreateDefault());
            SaveMessage = "已填入默认值，保存后生效";
        }

        [RelayCommand(CanExecute = nameof(CanCommitChanges))]
        private void Cancel()
        {
            DiscardChanges();
        }

        /// <summary>有未保存修改时才允许提交或取消。</summary>
        private bool CanCommitChanges() => IsDirty;

        [RelayCommand]
        private void AddTarget()
        {
            TargetsError = string.Empty;
            AddTargetDraft(new TargetDraft());
        }

        [RelayCommand]
        private void RemoveTarget(TargetDraft? draft)
        {
            if (draft is null)
            {
                return;
            }

            if (TargetDrafts.Count <= 1)
            {
                TargetsError = "至少保留 1 个监测目标";
                return;
            }

            TargetsError = string.Empty;
            draft.PropertyChanged -= OnTargetDraftChanged;
            TargetDrafts.Remove(draft);
        }

        [RelayCommand]
        private void BrowseDataDirectory()
        {
            var picked = _dialog.PickFolder(string.IsNullOrWhiteSpace(DataDirectory)
                ? AppPaths.Root
                : DataDirectory);

            if (!string.IsNullOrWhiteSpace(picked))
            {
                DataDirectory = picked;
            }
        }

        [RelayCommand]
        private void ResetDataDirectoryToDefault()
        {
            DataDirectory = string.Empty;
        }

        /// <summary>
        /// 保存主流程：校验 → （目录变更则）迁移 → 写配置 → 运行中按新配置重启调度。
        /// 任一步失败都不写坏配置、不切目录（DESIGN 第五节 2、第十一节 1）。
        /// </summary>
        private async Task<bool> SaveCoreAsync()
        {
            ClearInlineErrors();

            var draft = BuildDraft();
            var errors = CollectErrors(draft);
            if (errors.Count > 0)
            {
                ApplyErrors(errors);
                SaveMessage = $"配置校验未通过（{errors.Count} 项），未保存";
                return false;
            }

            var previousConfig = _config.Current;
            var effectiveDirectory = AppPaths.Root;

            // 数据目录变更：先迁移已有数据，失败保持原目录
            if (!string.Equals(NormalizeDirectory(draft.Storage.DataDirectory), NormalizeDirectory(AppPaths.Root),
                    StringComparison.OrdinalIgnoreCase))
            {
                var migration = await _migration.MigrateAsync(draft.Storage.DataDirectory);
                if (!migration.Success)
                {
                    DataDirectoryError = migration.Message ?? "数据目录迁移失败";
                    effectiveDirectory = migration.FromDirectory;
                    // 回退输入框到仍在生效的旧目录，避免界面与实际路径不一致
                    LoadDataDirectoryField(effectiveDirectory);
                    SaveMessage = $"数据目录迁移失败，已保持原目录：{migration.Message}";
                    return false;
                }

                effectiveDirectory = migration.ToDirectory;
            }

            if (!await _config.SaveAsync(draft))
            {
                SaveMessage = "配置写入失败，详见错误日志";
                _dialog.ShowError($"无法写入配置文件：{_config.ConfigFilePath}");
                return false;
            }

            LoadDataDirectoryField(effectiveDirectory);
            LoadFrom(_config.Current);

            await ApplyDetectionChangeAsync(previousConfig, draft);
            return true;
        }

        /// <summary>
        /// 运行中保存目标 / 检测参数 → 按新目标集合重启调度（DESIGN 第五节 2、DESIGN_S16_MULTITARGET 第六节）；
        /// 目标全部禁用时仅停止调度，不启动。
        /// </summary>
        private async Task ApplyDetectionChangeAsync(MonitorConfig previous, MonitorConfig current)
        {
            if (_scheduler.State == SchedulerState.Stopped || !DetectionSectionChanged(previous, current))
            {
                return;
            }

            await _scheduler.StopAsync();
            if (!current.HasEnabledTarget)
            {
                return;
            }

            await _scheduler.StartAsync(current.GetEnabledDetectionOptions(), current.Detection.Interval);
        }

        private static bool DetectionSectionChanged(MonitorConfig previous, MonitorConfig current)
            => previous.Detection.TimeoutMs != current.Detection.TimeoutMs
               || previous.Detection.PortPolicy != current.Detection.PortPolicy
               || previous.Detection.Interval != current.Detection.Interval
               || !TargetsEqual(previous.Targets, current.Targets);

        /// <summary>目标集合逐项比较（Id 不变即视为同一目标，历史记录归属不会断）。</summary>
        private static bool TargetsEqual(IReadOnlyList<TargetConfig> previous, IReadOnlyList<TargetConfig> current)
        {
            if (previous.Count != current.Count)
            {
                return false;
            }

            for (var i = 0; i < previous.Count; i++)
            {
                var before = previous[i];
                var after = current[i];
                if (before.Id != after.Id
                    || before.Enabled != after.Enabled
                    || before.Port != after.Port
                    || !string.Equals(before.TargetIp, after.TargetIp, StringComparison.Ordinal)
                    || !string.Equals(before.Name, after.Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private MonitorConfig BuildDraft()
        {
            var draft = _config.Current.Clone();

            draft.Targets = TargetDrafts.Select(t => t.ToConfig()).ToList();
            draft.Detection.Interval = new DetectionInterval(
                ParseInt(IntervalHoursText, 0), ParseInt(IntervalMinutesText, 0), ParseInt(IntervalSecondsText, 0));
            draft.Detection.TimeoutMs = ParseInt(TimeoutMsText, 0);
            draft.Detection.PortPolicy = PortPolicy;

            draft.Alert.ConsecutiveFailureThreshold = ParseInt(ThresholdText, 0);
            draft.Alert.CountPortFailure = CountPortFailure;
            draft.Alert.PopupEnabled = PopupEnabled;
            draft.Alert.TrayNotifyEnabled = TrayNotifyEnabled;

            draft.Storage.RetentionDays = ParseInt(RetentionDaysText, 0);
            draft.Storage.MaxInMemoryRecords = ParseInt(MaxInMemoryText, 0);
            var directory = DataDirectory.Trim();
            draft.Storage.DataDirectory = string.IsNullOrEmpty(directory)
                || string.Equals(NormalizeDirectory(directory), NormalizeDirectory(AppPaths.DefaultRoot),
                    StringComparison.OrdinalIgnoreCase)
                    ? null
                    : Path.GetFullPath(directory);

            draft.Interface.CloseMainWindowBehavior = CloseMainWindowBehavior;
            draft.Interface.ToolbarTopMost = ToolbarTopMost;
            draft.Interface.ToolbarAutoOnMinimize = ToolbarAutoOnMinimize;

            draft.Startup.AutoStartEnabled = AutoStartEnabled;
            draft.Startup.AutoStartInToolbar = AutoStartInToolbar;

            return draft;
        }

        /// <summary>字段级校验错误：数值解析失败先给出，其余交给配置服务（同一套字段标识）。</summary>
        private List<ConfigValidationError> CollectErrors(MonitorConfig draft)
        {
            var errors = new List<ConfigValidationError>();

            // 端口文本非空但无法解析：行内提示所属目标（合法但越界的值交给配置服务拦截）
            for (var i = 0; i < TargetDrafts.Count; i++)
            {
                var draftPort = TargetDrafts[i].PortText;
                if (!string.IsNullOrWhiteSpace(draftPort) && !int.TryParse(draftPort.Trim(), out _))
                {
                    errors.Add(new($"targets[{i}].port", "端口须为 1~65535 的整数，留空表示不检测端口"));
                }
            }

            if (HasNonPositiveParseFailure(IntervalHoursText)
                || HasNonPositiveParseFailure(IntervalMinutesText)
                || HasNonPositiveParseFailure(IntervalSecondsText)
                || !draft.Detection.Interval.IsValid)
            {
                errors.Add(new("detection.interval", "检测间隔须为 0 以上的时 / 分 / 秒，且总时长大于 0"));
            }

            if (HasNonPositiveParseFailure(TimeoutMsText))
            {
                errors.Add(new("detection.timeoutMs", "超时时间须为大于 0 的整数（毫秒）"));
            }

            if (HasNonPositiveParseFailure(ThresholdText))
            {
                errors.Add(new("alert.threshold", "连续失败告警阈值须为大于 0 的整数"));
            }

            if (HasNonPositiveParseFailure(RetentionDaysText))
            {
                errors.Add(new("storage.retentionDays", "历史保留天数须为 1~3650 的整数"));
            }

            if (HasNonPositiveParseFailure(MaxInMemoryText))
            {
                errors.Add(new("storage.maxInMemoryRecords", "内存最大条数须为大于 0 的整数"));
            }

            errors.AddRange(_config.Validate(draft));

            // 同一字段只保留首条提示（解析错误优先于服务校验）
            return errors.DistinctBy(e => e.Field).ToList();
        }

        private void ApplyErrors(IReadOnlyList<ConfigValidationError> errors)
        {
            foreach (var error in errors)
            {
                switch (error.Field)
                {
                    case "targets": TargetsError = error.Message; break;
                    case "detection.interval": IntervalError = error.Message; break;
                    case "detection.timeoutMs": TimeoutError = error.Message; break;
                    case "alert.threshold": ThresholdError = error.Message; break;
                    case "storage.retentionDays": RetentionDaysError = error.Message; break;
                    case "storage.maxInMemoryRecords": MaxInMemoryError = error.Message; break;
                    case "storage.dataDirectory": DataDirectoryError = error.Message; break;
                    default: ApplyTargetError(error); break;
                }
            }
        }

        /// <summary>目标行错误落到对应行（字段形如 targets[2].targetIp / targets[2].port）。</summary>
        private void ApplyTargetError(ConfigValidationError error)
        {
            const string prefix = "targets[";
            if (!error.Field.StartsWith(prefix, StringComparison.Ordinal))
            {
                return;
            }

            var close = error.Field.IndexOf(']');
            if (close < 0
                || !int.TryParse(error.Field.AsSpan(prefix.Length, close - prefix.Length), out var index)
                || index < 0 || index >= TargetDrafts.Count)
            {
                return;
            }

            var draft = TargetDrafts[index];
            draft.Error = string.IsNullOrEmpty(draft.Error) ? error.Message : $"{draft.Error}；{error.Message}";
        }

        private void ClearInlineErrors()
        {
            TargetsError = IntervalError = TimeoutError = string.Empty;
            ThresholdError = RetentionDaysError = MaxInMemoryError = DataDirectoryError = string.Empty;
            foreach (var draft in TargetDrafts)
            {
                draft.Error = string.Empty;
            }
        }

        private void LoadFrom(MonitorConfig config)
        {
            _loading = true;
            ClearInlineErrors();

            ReplaceTargetDrafts(config.Targets);
            IntervalHoursText = config.Detection.Interval.Hours.ToString();
            IntervalMinutesText = config.Detection.Interval.Minutes.ToString();
            IntervalSecondsText = config.Detection.Interval.Seconds.ToString();
            TimeoutMsText = config.Detection.TimeoutMs.ToString();
            PortPolicy = config.Detection.PortPolicy;

            ThresholdText = config.Alert.ConsecutiveFailureThreshold.ToString();
            CountPortFailure = config.Alert.CountPortFailure;
            PopupEnabled = config.Alert.PopupEnabled;
            TrayNotifyEnabled = config.Alert.TrayNotifyEnabled;

            RetentionDaysText = config.Storage.RetentionDays.ToString();
            MaxInMemoryText = config.Storage.MaxInMemoryRecords.ToString();
            LoadDataDirectoryField(AppPaths.Root);

            CloseMainWindowBehavior = config.Interface.CloseMainWindowBehavior;
            ToolbarTopMost = config.Interface.ToolbarTopMost;
            ToolbarAutoOnMinimize = config.Interface.ToolbarAutoOnMinimize;

            AutoStartEnabled = config.Startup.AutoStartEnabled;
            AutoStartInToolbar = config.Startup.AutoStartInToolbar;

            SaveMessage = string.Empty;
            _loading = false;
            RefreshDirtyState();
        }

        /// <summary>用配置值重建目标行草稿（加载 / 放弃修改 / 恢复默认均走这里）。</summary>
        private void ReplaceTargetDrafts(IReadOnlyList<TargetConfig> targets)
        {
            foreach (var draft in TargetDrafts)
            {
                draft.PropertyChanged -= OnTargetDraftChanged;
            }

            TargetDrafts.Clear();
            foreach (var target in targets)
            {
                AddTargetDraft(new TargetDraft(target));
            }
        }

        private void AddTargetDraft(TargetDraft draft)
        {
            draft.PropertyChanged += OnTargetDraftChanged;
            TargetDrafts.Add(draft);
        }

        private void OnTargetDraftChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 行内错误提示变化不代表配置内容变化
            if (e.PropertyName == nameof(TargetDraft.Error))
            {
                return;
            }

            RefreshDirtyState();
        }

        /// <summary>数据目录输入框展示当前生效路径；默认目录以留空表达（与配置中 null 语义一致）。</summary>
        private void LoadDataDirectoryField(string effectiveRoot)
        {
            DataDirectory = string.Equals(NormalizeDirectory(effectiveRoot),
                NormalizeDirectory(AppPaths.DefaultRoot), StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : effectiveRoot;
        }

        /// <summary>
        /// 脏标记以「草稿序列化结果与当前生效配置是否一致」判定：
        /// 改回原值即视为未修改，避免仅靠变更计数产生的假脏。
        /// </summary>
        private bool ComputeDirty()
            => JsonSerializer.Serialize(BuildDraft()) != JsonSerializer.Serialize(_config.Current);

        private void RefreshDirtyState()
        {
            if (_loading)
            {
                return;
            }

            IsDirty = ComputeDirty();
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(IsDirty))
            {
                return;
            }

            RefreshDirtyState();
        }

        private static int ParseInt(string? text, int fallback)
            => int.TryParse(text?.Trim(), out var value) ? value : fallback;

        /// <summary>输入非空但解析失败，或解析出非正值 —— 均视为需要提示的非法数值。</summary>
        private static bool HasNonPositiveParseFailure(string? text)
            => !string.IsNullOrWhiteSpace(text) && (!int.TryParse(text.Trim(), out var value) || value < 0);

        private static string NormalizeDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return AppPaths.DefaultRoot;
            }

            try
            {
                return Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return directory;
            }
        }

        /// <summary>下拉框选项：值为枚举，展示为中文（避免界面直接暴露英文枚举名）。</summary>
        public sealed record EnumOption<T>(T Value, string Label);

        /// <summary>
        /// 目标行草稿（DESIGN_S16_MULTITARGET 第三节）：保存时转为 <see cref="TargetConfig"/>；
        /// Id 保留以保证编辑目标后历史记录仍归属同一目标。
        /// </summary>
        public sealed partial class TargetDraft : ObservableObject
        {
            public TargetDraft() : this(new TargetConfig())
            {
            }

            public TargetDraft(TargetConfig target)
            {
                Id = string.IsNullOrWhiteSpace(target.Id) ? Guid.NewGuid().ToString("N") : target.Id;
                _name = target.Name ?? string.Empty;
                _targetIp = target.TargetIp;
                _portText = target.Port?.ToString() ?? string.Empty;
                _enabled = target.Enabled;
            }

            public string Id { get; }

            [ObservableProperty] private string _name;
            [ObservableProperty] private string _targetIp;
            [ObservableProperty] private string _portText;
            [ObservableProperty] private bool _enabled;

            /// <summary>行内校验提示（地址非法 / 端口非法 / 重复目标）。</summary>
            [ObservableProperty] private string _error = string.Empty;

            public TargetConfig ToConfig() => new()
            {
                Id = Id,
                Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
                TargetIp = TargetIp.Trim(),
                Port = int.TryParse(PortText?.Trim(), out var port) ? port : null,
                Enabled = Enabled,
            };
        }
    }
}
