using System.Text.Json.Serialization;

namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 检测周期（时 / 分 / 秒），对应 DESIGN 第四节设置页 b) 检测设置的间隔。
    /// </summary>
    public readonly record struct DetectionInterval(int Hours, int Minutes, int Seconds)
    {
        /// <summary>总时长；非法（负数）视为 0。</summary>
        public TimeSpan ToTimeSpan()
        {
            var ticks = (long)Hours * 3600 + (long)Minutes * 60 + Seconds;
            return ticks > 0 ? TimeSpan.FromSeconds(ticks) : TimeSpan.Zero;
        }

        /// <summary>间隔必须大于 0（DESIGN 第五节保存校验）；派生值不入配置文件。</summary>
        [JsonIgnore]
        public bool IsValid => ToTimeSpan() > TimeSpan.Zero;

        public override string ToString() => $"{Hours:00}:{Minutes:00}:{Seconds:00}";
    }
}
