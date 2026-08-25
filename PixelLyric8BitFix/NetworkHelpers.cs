using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 给 app 里所有会发外网请求的 HttpClient 用的共享创建方法——统一强制走 IPv4，绕开一个实测踩到过的
    /// .NET 连接坑：机器上某个域名（不挑域名，是网络层面的毛病）解析得出 IPv6 地址，但这条 IPv6 路由
    /// 实际连不通（能解析不代表能连通，常见于路由器发了 IPv6 前缀但没真正拉通公网这种情况）。浏览器的
    /// Happy Eyeballs（RFC 8305）实现更激进，IPv6 卡住几百毫秒就立刻改走 IPv4；.NET 默认的连接逻辑在
    /// 这种"地址存在但连不通"上容错要差不少，实测会一路干等到这个 HttpClient 自己配置的 Timeout 才失败，
    /// 同一个请求直接用浏览器打开却是秒开——查双语翻译"开了开关但一直没有翻译行"那次查出来的。
    /// 这不是 Google 一家域名的事，是机器/网络层面的问题，所以 app 里所有对外请求的 HttpClient
    /// （歌词抓取、翻译、检查更新、下载安装包）都该用这同一份，不要各自再手写一遍这段连接逻辑。
    /// </summary>
    internal static class NetworkHelpers
    {
        /// <summary>建一个强制走 IPv4、带指定超时的 HttpClient——app 里所有会发外网请求的地方都该用这个，
        /// 不要再各自 new HttpClient() 了。</summary>
        public static HttpClient CreateHttpClient(TimeSpan timeout) => new(CreateIPv4Handler()) { Timeout = timeout };

        private static SocketsHttpHandler CreateIPv4Handler() => new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, AddressFamily.InterNetwork, cancellationToken);
                if (addresses.Length == 0)
                {
                    throw new SocketException((int)SocketError.HostNotFound);
                }

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addresses[0], context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }
}
