using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkMonitorTool.Models;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// JSON 配置读写实现（DESIGN 第五节 1）。
    /// 文件固定位于默认根目录下的 appsettings.json，字段名 camelCase；
    /// 读取时任何缺失字段 / 损坏内容都回退默认值。
    /// </summary>
    public sealed class JsonConfigService : IConfigService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly ILogger<JsonConfigService>? _logger;
        private MonitorConfig _current = MonitorConfig.CreateDefault();
        private bool _loaded;

        public JsonConfigService(ILogger<JsonConfigService>? logger = null)
        {
            _logger = logger;
        }

        public string ConfigFilePath => AppPaths.ConfigFile;

        public event EventHandler<MonitorConfig>? ConfigChanged;

        public MonitorConfig Current
        {
            get
            {
                if (!_loaded)
                {
                    Load();
                }

                return _current;
            }
        }

        public MonitorConfig Load()
        {
            var config = ReadFromDisk() ?? MonitorConfig.CreateDefault();
            Migrate(config);

            // 数据目录：合法则生效，非法 / 不可用则回退默认目录，保证可启动（DESIGN 第十一节 1）
            AppPaths.Root = ResolveDataDirectory(config.Storage.DataDirectory);
            _current = config;
            _loaded = true;
            return _current;
        }

        public async Task<bool> SaveAsync(MonitorConfig config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);

            Migrate(config);
            config.ConfigVersion = MonitorConfig.CurrentVersion;
            AppPaths.Root = ResolveDataDirectory(config.Storage.DataDirectory);
            _current = config;
            _loaded = true;

            try
            {
                Directory.CreateDirectory(AppPaths.DefaultRoot);
                var json = JsonSerializer.Serialize(config, SerializerOptions);

                // 先写临时文件再替换，避免写入中断留下半截配置
                var temp = ConfigFilePath + ".tmp";
                await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
                File.Move(temp, ConfigFilePath, overwrite: true);

                // 界面摘要刷新依此事件（DESIGN 第五节 2「保存后生效」）
                ConfigChanged?.Invoke(this, config);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "配置文件写入失败：{Path}", ConfigFilePath);
                return false;
            }
        }

        public IReadOnlyList<ConfigValidationError> Validate(MonitorConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var errors = new List<ConfigValidationError>();

            ValidateTargets(config, errors);

            if (!config.Detection.Interval.IsValid)
            {
                errors.Add(new("detection.interval", "检测间隔必须大于 0"));
            }

            if (config.Detection.TimeoutMs <= 0)
            {
                errors.Add(new("detection.timeoutMs", "超时时间必须大于 0 毫秒"));
            }

            if (config.Alert.ConsecutiveFailureThreshold <= 0)
            {
                errors.Add(new("alert.threshold", "连续失败告警阈值必须大于 0"));
            }

            if (config.Storage.RetentionDays is < 1 or > 3650)
            {
                errors.Add(new("storage.retentionDays", "历史保留天数须在 1~3650 之间"));
            }

            if (config.Storage.MaxInMemoryRecords <= 0)
            {
                errors.Add(new("storage.maxInMemoryRecords", "内存最大条数必须大于 0"));
            }

            if (!DataDirectoryValidator.TryValidate(config.Storage.DataDirectory, out var directoryError))
            {
                errors.Add(new("storage.dataDirectory", directoryError ?? "数据目录不可用"));
            }

            return errors;
        }

        public MonitorConfig CreateDefault() => MonitorConfig.CreateDefault();

        /// <summary>
        /// 配置结构迁移（DESIGN_S16_MULTITARGET 第四节）：
        /// v1 无 targets 时用旧单目标字段构造唯一目标（Id 固定为 default），随后清理迁移输入字段。
        /// 幂等，重复读取不会重复创建目标。
        /// </summary>
        private static void Migrate(MonitorConfig config)
        {
            config.Detection ??= new DetectionConfig();
            config.Targets ??= new List<TargetConfig>();

            if (config.Targets.Count == 0)
            {
                var legacyIp = config.Detection.LegacyTargetIp?.Trim();
                config.Targets.Add(new TargetConfig
                {
                    Id = TargetConfig.LegacyTargetId,
                    TargetIp = string.IsNullOrEmpty(legacyIp) ? "127.0.0.1" : legacyIp,
                    Port = config.Detection.LegacyPort is >= 1 and <= 65535
                        ? config.Detection.LegacyPort
                        : null,
                });
            }

            // 迁移输入用完即弃：不再写出 detection.targetIp / port
            config.Detection.LegacyTargetIp = null;
            config.Detection.LegacyPort = null;
            config.ConfigVersion = MonitorConfig.CurrentVersion;
        }

        /// <summary>目标列表校验：至少一个、地址合法、端口范围、同一地址+端口不重复。</summary>
        private static void ValidateTargets(MonitorConfig config, List<ConfigValidationError> errors)
        {
            if (config.Targets.Count == 0)
            {
                errors.Add(new("targets", "至少需要一个监测目标"));
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < config.Targets.Count; i++)
            {
                var target = config.Targets[i];
                if (string.IsNullOrWhiteSpace(target.TargetIp))
                {
                    errors.Add(new($"targets[{i}].targetIp", "目标地址不能为空"));
                }
                else if (!IsValidTarget(target.TargetIp))
                {
                    errors.Add(new($"targets[{i}].targetIp", "目标地址不是合法的 IP 或主机名"));
                }

                if (target.Port is < 1 or > 65535)
                {
                    errors.Add(new($"targets[{i}].port", "端口须在 1~65535 之间，留空表示不检测端口"));
                }
                else if (!seen.Add($"{target.TargetIp.Trim()}:{target.Port?.ToString() ?? string.Empty}"))
                {
                    errors.Add(new($"targets[{i}].targetIp", "同一目标地址与端口不能重复添加"));
                }
            }
        }

        /// <summary>读取配置文件；文件缺失、损坏或反序列化失败时返回 null（由调用方取默认）。</summary>
        private MonitorConfig? ReadFromDisk()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    return null;
                }

                var json = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize<MonitorConfig>(json, SerializerOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "配置文件读取失败，使用默认配置：{Path}", ConfigFilePath);
                return null;
            }
        }

        /// <summary>
        /// 校验配置中的数据目录：合法则返回该目录，非法或不可用回退默认目录（不因坏配置导致无法启动）。
        /// </summary>
        private string ResolveDataDirectory(string? configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return AppPaths.DefaultRoot;
            }

            if (DataDirectoryValidator.TryValidate(configured, out var error))
            {
                return Path.GetFullPath(configured);
            }

            _logger?.LogWarning("配置的 dataDirectory 不可用，回退默认目录：{Error}", error);
            return AppPaths.DefaultRoot;
        }

        /// <summary>目标地址合法性：IPv4 / IPv6 或 RFC 主机名（DESIGN 第五节保存校验）。</summary>
        private static bool IsValidTarget(string target)
        {
            var value = target.Trim();
            if (value.Length is 0 or > 253)
            {
                return false;
            }

            if (IPAddress.TryParse(value, out _))
            {
                return true;
            }

            // 主机名：仅允许字母数字、连字符、下划线与点分隔；
            // 含冒号 / 斜杠 / 空格等一律判非法（IPv6 已在上面命中）。
            foreach (var ch in value)
            {
                if (!char.IsAsciiLetterOrDigit(ch) && ch != '-' && ch != '_' && ch != '.')
                {
                    return false;
                }
            }

            foreach (var label in value.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (label.Length > 63 || label.StartsWith('-') || label.EndsWith('-'))
                {
                    return false;
                }
            }

            return value[^1] != '-' && value[0] != '-';
        }
    }
}
