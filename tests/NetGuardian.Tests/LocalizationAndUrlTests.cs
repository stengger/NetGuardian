using NetGuardian.Core.Localization;
using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.Tests;

public class WebsiteUrlTests
{
    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("http://example.com", "http://example.com")]
    public void NormalizeUrl_AddsHttpsWhenSchemeMissing(string input, string expected)
    {
        Assert.Equal(expected, WebsiteTestService.NormalizeUrl(input));
    }
}

public class LocalizationTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 空状态错误和确认文案不能显示内部资源键(string language)
    {
        var localizer = new Localizer();
        localizer.SetLanguage(language);
        foreach (var key in new[] { "ActionFailedTitle", "NoAdapterRows", "NoDnsRows", "NoIpInfoRows", "NoQualityRows", "NoReportRows", "NoWebsiteRows", "ScheduledStartupDisableConfirmMessage", "ScheduledStartupDisableConfirmTitle", "Undo", "UnhandledErrorMessage", "UnhandledErrorTitle", "WifiProfileDeletedMessage", "WifiProfileDeletedTitle" })
        {
            Assert.Contains(key, localizer.KeysForLanguage(language));
            Assert.False(string.IsNullOrWhiteSpace(localizer.T(key)));
        }
    }

    [Fact]
    public void SupportedLanguages_AreChineseAndEnglish()
    {
        var localizer = new Localizer();
        var languages = localizer.SupportedLanguages.ToList();

        Assert.Equal(2, languages.Count);
        Assert.Contains("zh-CN", languages);
        Assert.Contains("en-US", languages);
    }

    [Fact]
    public void ResourceKeys_MatchAcrossLanguages()
    {
        var localizer = new Localizer();
        var zhKeys = localizer.KeysForLanguage("zh-CN").OrderBy(key => key).ToList();
        var enKeys = localizer.KeysForLanguage("en-US").OrderBy(key => key).ToList();

        var missingInEnglish = zhKeys.Except(enKeys, StringComparer.OrdinalIgnoreCase).ToList();
        var missingInChinese = enKeys.Except(zhKeys, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(missingInEnglish.Count == 0 && missingInChinese.Count == 0,
            $"Localization keys differ. Missing in English: {string.Join(", ", missingInEnglish)}. " +
            $"Missing in Chinese: {string.Join(", ", missingInChinese)}.");
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void UiPolishStrings_AreLocalized(string language)
    {
        var localizer = new Localizer();
        localizer.SetLanguage(language);

        foreach (var key in new[] { "NoNearbyWifiRows", "NoPriorityWifiRows", "NoRecentEvents" })
        {
            var value = localizer.T(key);
            Assert.False(string.IsNullOrWhiteSpace(value) || value.Equals(key, StringComparison.OrdinalIgnoreCase),
                $"Expected localized value for {key} in {language}.");
        }
    }
}
