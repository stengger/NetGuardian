using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NetGuardian.Core.Network;

public sealed class PublicIpInfoService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private PublicIpInfo? _cachedInfo;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public PublicIpInfoService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<PublicIpInfo> GetAsync(CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedInfo is not null && DateTimeOffset.Now - _cachedAt < CacheDuration)
            {
                return _cachedInfo;
            }

            try
            {
                return Cache(await QueryIpWhoAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                try
                {
                    return Cache(await QueryIpifyAsync(cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception fallbackEx) when (fallbackEx is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    throw new InvalidOperationException("All public IP providers failed.", fallbackEx);
                }
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private PublicIpInfo Cache(PublicIpInfo info)
    {
        _cachedInfo = info;
        _cachedAt = DateTimeOffset.Now;
        return info;
    }

    private async Task<PublicIpInfo> QueryIpWhoAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<IpWhoResponse>(
            "https://ipwho.is/?lang=zh-CN",
            cancellationToken);

        if (response is null)
        {
            throw new InvalidOperationException("No IP information returned.");
        }

        if (response.Success == false)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message) ? "IP information lookup failed." : response.Message);
        }

        var autonomousSystem = response.Connection?.Asn is > 0 ? $"AS{response.Connection.Asn}" : "-";
        return new PublicIpInfo(
            response.Ip ?? "-",
            response.Country ?? "-",
            response.CountryCode ?? "-",
            response.Region ?? "-",
            response.City ?? "-",
            response.Connection?.Isp ?? "-",
            response.Connection?.Org ?? "-",
            autonomousSystem,
            response.Timezone?.Id ?? "-",
            response.Latitude,
            response.Longitude,
            DateTimeOffset.Now);
    }

    private async Task<PublicIpInfo> QueryIpifyAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<IpifyResponse>(
            "https://api.ipify.org?format=json",
            cancellationToken);

        if (response is null || string.IsNullOrWhiteSpace(response.Ip))
        {
            throw new InvalidOperationException("No public IP returned.");
        }

        return new PublicIpInfo(
            response.Ip,
            "-",
            "-",
            "-",
            "-",
            "-",
            "-",
            "-",
            "-",
            null,
            null,
            DateTimeOffset.Now);
    }

    private sealed class IpWhoResponse
    {
        [JsonPropertyName("success")]
        public bool? Success { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("ip")]
        public string? Ip { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; init; }

        [JsonPropertyName("region")]
        public string? Region { get; init; }

        [JsonPropertyName("city")]
        public string? City { get; init; }

        [JsonPropertyName("connection")]
        public IpWhoConnection? Connection { get; init; }

        [JsonPropertyName("timezone")]
        public IpWhoTimezone? Timezone { get; init; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; init; }
    }

    private sealed class IpWhoConnection
    {
        [JsonPropertyName("asn")]
        public int Asn { get; init; }

        [JsonPropertyName("org")]
        public string? Org { get; init; }

        [JsonPropertyName("isp")]
        public string? Isp { get; init; }
    }

    private sealed class IpWhoTimezone
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed class IpifyResponse
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; init; }
    }
}

public sealed record PublicIpInfo(
    string Ip,
    string Country,
    string CountryCode,
    string Region,
    string City,
    string Isp,
    string Organization,
    string AutonomousSystem,
    string Timezone,
    double? Latitude,
    double? Longitude,
    DateTimeOffset RefreshedAt);
