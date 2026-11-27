using System.Net;
using System.Net.Sockets;

namespace NetGuardian.Core.Connectivity;

internal static class WifiSocketConnector
{
    internal static SocketsHttpHandler CreateHandler(
        IPAddress address,
        int interfaceIndex,
        bool allowAutoRedirect = false,
        CookieContainer? cookies = null)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            UseProxy = false,
            UseCookies = cookies is not null,
            ConnectCallback = (context, token) => ConnectAsync(context.DnsEndPoint, address, interfaceIndex, token)
        };
        if (cookies is not null)
            handler.CookieContainer = cookies;
        return handler;
    }

    internal static HttpClient CreateHttpClient(
        IPAddress address,
        int interfaceIndex,
        bool allowAutoRedirect = false,
        CookieContainer? cookies = null) =>
        new(CreateHandler(address, interfaceIndex, allowAutoRedirect, cookies));

    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        IPAddress address,
        int interfaceIndex,
        CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, AddressFamily.InterNetwork, token);
        SocketException? failure = null;
        foreach (var remote in addresses)
        {
            token.ThrowIfCancellationRequested();
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                // Windows IP_UNICAST_IF（31）接收网络字节序的索引，限制实际出站接口。
                socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(interfaceIndex));
                socket.Bind(new IPEndPoint(address, 0));
                await socket.ConnectAsync(new IPEndPoint(remote, endpoint.Port), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                failure = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw failure ?? new SocketException((int)SocketError.HostNotFound);
    }
}
