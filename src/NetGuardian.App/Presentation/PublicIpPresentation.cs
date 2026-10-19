using System;

namespace NetGuardian.App.Presentation;

internal static class PublicIpPresentation
{
    public static string Coordinates(double? latitude, double? longitude, string unknown)
        => latitude is { } lat && longitude is { } lon
            && double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180
            ? $"{lat:F4}, {lon:F4}" : unknown;
}
