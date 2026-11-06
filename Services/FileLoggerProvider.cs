using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 轻量文件日志 Provider：按天写入日志目录（默认保留 7 天）。
    /// S1 仅服务于全局异常与运行日志，后续迭代复用。
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
        private readonly string _directory;
        private readonly string _filePrefix;
        private readonly int _retainedDays;

        public FileLoggerProvider(string directory, string filePrefix = "app", int retainedDays = 7)
        {
            _directory = directory;
            _filePrefix = filePrefix;
            _retainedDays = retainedDays;
            Directory.CreateDirectory(_directory);
            CleanupOldFiles();
        }

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new FileLogger(name, Write));

        /// <summary>串行化写入，避免文件占用冲突</summary>
        private readonly object _sync = new();

        private void Write(string line)
        {
            try
            {
                lock (_sync)
                {
                    var path = Path.Combine(_directory, $"{_filePrefix}-{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写入失败不得影响业务流程
            }
        }

        private void CleanupOldFiles()
        {
            try
            {
                var threshold = DateTime.Now.Date.AddDays(-_retainedDays);
                foreach (var file in Directory.GetFiles(_directory, $"{_filePrefix}-*.log"))
                {
                    if (File.GetLastWriteTime(file) < threshold)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // 清理失败忽略
            }
        }

        public void Dispose() => _loggers.Clear();

        private sealed class FileLogger(string category, Action<string> write) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] [{category}] {formatter(state, exception)}"
                    + (exception is null ? string.Empty : $"{Environment.NewLine}{exception}"));
            }
        }
    }
}
