using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NetGuardian.Core.Network;

public sealed class PublicIpInfoService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public async Task<PublicIpInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        var response = await SharedHttpClient.GetFromJsonAsync<IpApiResponse>(
            "http://ip-api.com/json/?fields=status,message,country,countryCode,regionName,city,isp,org,as,query,lat,lon,timezone&lang=zh-CN",
            cancellationToken);

        if (response is null)
        {
            throw new InvalidOperationException("No IP information returned.");
        }

        if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message) ? "IP information lookup failed." : response.Message);
        }

        return new PublicIpInfo(
            response.Query ?? "-",
            response.Country ?? "-",
            response.CountryCode ?? "-",
            response.RegionName ?? "-",
            response.City ?? "-",
            response.Isp ?? "-",
            response.Org ?? "-",
            response.As ?? "-",
            response.Timezone ?? "-",
            response.Lat,
            response.Lon,
            DateTimeOffset.Now);
    }

    private sealed class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("query")]
        public string? Query { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("countryCode")]
        public string? CountryCode { get; init; }

        [JsonPropertyName("regionName")]
        public string? RegionName { get; init; }

        [JsonPropertyName("city")]
        public string? City { get; init; }

        [JsonPropertyName("isp")]
        public string? Isp { get; init; }

        [JsonPropertyName("org")]
        public string? Org { get; init; }

        [JsonPropertyName("as")]
        public string? As { get; init; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; init; }

        [JsonPropertyName("lat")]
        public double Lat { get; init; }

        [JsonPropertyName("lon")]
        public double Lon { get; init; }
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
    double Latitude,
    double Longitude,
    DateTimeOffset RefreshedAt);
