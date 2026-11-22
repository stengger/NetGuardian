using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NetGuardian.Tests;

// 仅监听回环地址。每次测试创建临时 TLS 身份，客户端只接受本次证书。
internal sealed class UpdateLoopbackServer : IAsyncDisposable
{
    internal sealed record Reply(int Status, byte[] Body, int? Advertised = null, string? ExtraHeader = null);
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentBag<Task> connections = new();
    private readonly Task accept;
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal X509Certificate2 Certificate { get; }
    internal Uri Root { get; }
    internal ConcurrentQueue<(string Path, string? Range)> Requests { get; } = new();
    internal Func<string, string?, Reply> Respond { get; set; } = (_, _) => new(503, []);

    internal UpdateLoopbackServer()
    {
        var csr = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using var temporary = csr.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // 让 Windows Schannel 能引用测试私钥，证书只用于本次回环服务。
        Certificate = new X509Certificate2(temporary.Export(X509ContentType.Pfx));
        listener.Start();
        Root = new Uri($"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        accept = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
                connections.Add(ServeAsync(await listener.AcceptTcpClientAsync(stop.Token)));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient socket)
    {
        using (socket)
        using (var tls = new SslStream(socket.GetStream()))
        {
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Certificate }, stop.Token);
                using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                string? line = await reader.ReadLineAsync(stop.Token);
                if (line is null) return;
                string path = line.Split(' ')[1];
                string? range = null;
                while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 } header)
                    if (header.StartsWith("Range: ", StringComparison.OrdinalIgnoreCase)) range = header[7..];
                Requests.Enqueue((path, range));
                var reply = Respond(path, range);
                var headers = $"HTTP/1.1 {reply.Status} Test\r\nContent-Length: {reply.Advertised ?? reply.Body.Length}\r\nConnection: close\r\n{reply.ExtraHeader}\r\n";
                await tls.WriteAsync(Encoding.ASCII.GetBytes(headers), stop.Token);
                await tls.WriteAsync(reply.Body, stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await accept;
        listener.Stop();
        await Task.WhenAll(connections);
        Certificate.Dispose(); key.Dispose(); stop.Dispose();
    }
}
