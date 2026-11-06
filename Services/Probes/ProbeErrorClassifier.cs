using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkMonitorTool.Services.Probes
{
    /// <summary>
    /// 将底层网络异常 / 状态归类为面向用户的错误摘要，
    /// 区分超时 / 不可达 / 权限等（DESIGN 第十四节第 2 条）。
    /// </summary>
    internal static class ProbeErrorClassifier
    {
        public const string Timeout = "检测超时";
        public const string Unreachable = "目标不可达";
        public const string AccessDenied = "权限不足";
        public const string DnsFailure = "地址解析失败";
        public const string Refused = "端口拒绝连接";

        /// <summary>从异常归类错误摘要。</summary>
        public static string FromException(Exception ex)
        {
            // 拆包 PingException 等包装异常
            var inner = ex is PingException && ex.InnerException is not null
                ? ex.InnerException
                : ex;

            if (inner is SocketException sock)
            {
                return sock.SocketErrorCode switch
                {
                    SocketError.TimedOut => Timeout,
                    SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData => DnsFailure,
                    SocketError.NetworkUnreachable or SocketError.HostUnreachable
                        or SocketError.NetworkDown or SocketError.ConnectionReset => Unreachable,
                    SocketError.AccessDenied => AccessDenied,
                    SocketError.ConnectionRefused => Refused,
                    _ => $"{sock.SocketErrorCode}",
                };
            }

            if (inner is OperationCanceledException)
            {
                return Timeout;
            }

            // 依据常见消息兜底判断（权限 / 不可达）
            var message = inner.Message;
            if (message.Contains("access", StringComparison.OrdinalIgnoreCase)
                || message.Contains("permission", StringComparison.OrdinalIgnoreCase)
                || message.Contains("拒绝", StringComparison.Ordinal)
                || message.Contains("权限", StringComparison.Ordinal))
            {
                return AccessDenied;
            }

            return string.IsNullOrWhiteSpace(message) ? inner.GetType().Name : message;
        }
    }
}
