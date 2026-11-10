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

            LoadFrom(_config.Current);
        }

        public override string Key => "settings";

        public override string Title => "设置";

        public IReadOnlyList<EnumOption<PortDetectionPolicy>> PortPolicyOptions { get; }

        public IReadOnlyList<EnumOption<CloseWindowBehavior>> CloseBehaviorOptions { get; }

        // ---- a) 目标设置 ----

        [ObservableProperty] private string _targetIp = string.Empty;
        [ObservableProperty] private string _portText = string.Empty;

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

        [ObservableProperty] private string _targetIpError = string.Empty;
        [ObservableProperty] private string _portError = string.Empty;
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

            var previousDetection = _config.Current.Detection;
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

            await ApplyDetectionChangeAsync(previousDetection, draft.Detection);
            return true;
        }

        /// <summary>运行中保存检测参数 → 按新配置重启调度周期（DESIGN 第五节 2）。</summary>
        private async Task ApplyDetectionChangeAsync(DetectionConfig previous, DetectionConfig current)
        {
            if (_scheduler.State == SchedulerState.Stopped || !DetectionSectionChanged(previous, current))
            {
                return;
            }

            await _scheduler.StopAsync();
            await _scheduler.StartAsync(current.ToDetectionOptions(), current.Interval);
        }

        private static bool DetectionSectionChanged(DetectionConfig previous, DetectionConfig current)
            => previous.TargetIp != current.TargetIp
               || previous.Port != current.Port
               || previous.TimeoutMs != current.TimeoutMs
               || previous.PortPolicy != current.PortPolicy
               || previous.Interval != current.Interval;

        private MonitorConfig BuildDraft()
        {
            var draft = _config.Current.Clone();

            draft.Detection.TargetIp = TargetIp.Trim();
            draft.Detection.Port = ParseNullableInt(PortText, out _);
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

            if (!string.IsNullOrWhiteSpace(PortText) && draft.Detection.Port is null)
            {
                errors.Add(new("detection.port", "端口须为 1~65535 的整数，留空表示不检测端口"));
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
                    case "detection.targetIp": TargetIpError = error.Message; break;
                    case "detection.port": PortError = error.Message; break;
                    case "detection.interval": IntervalError = error.Message; break;
                    case "detection.timeoutMs": TimeoutError = error.Message; break;
                    case "alert.threshold": ThresholdError = error.Message; break;
                    case "storage.retentionDays": RetentionDaysError = error.Message; break;
                    case "storage.maxInMemoryRecords": MaxInMemoryError = error.Message; break;
                    case "storage.dataDirectory": DataDirectoryError = error.Message; break;
                }
            }
        }

        private void ClearInlineErrors()
        {
            TargetIpError = PortError = IntervalError = TimeoutError = string.Empty;
            ThresholdError = RetentionDaysError = MaxInMemoryError = DataDirectoryError = string.Empty;
        }

        private void LoadFrom(MonitorConfig config)
        {
            _loading = true;
            ClearInlineErrors();

            TargetIp = config.Detection.TargetIp;
            PortText = config.Detection.Port?.ToString() ?? string.Empty;
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

        private static int? ParseNullableInt(string? text, out bool parsed)
        {
            var trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                parsed = true;
                return null;
            }

            parsed = int.TryParse(trimmed, out var value);
            return parsed ? value : null;
        }

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
    }
}
