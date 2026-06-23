# Project Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve NetGuardian reliability and UI clarity without changing core network recovery behavior.

**Architecture:** Keep the current WPF app structure and add narrow, tested surfaces only where they reduce risk. Core-only logic stays in `NetGuardian.Core`; WPF-specific polish stays in `NetGuardian.App`. The existing console test project remains the verification harness.

**Tech Stack:** C# 12, .NET 8, WPF, WPF-UI, lightweight console tests in `tests/NetGuardian.Tests`.

---

## File Structure

- Modify: `src/NetGuardian.Core/Localization/Localizer.cs`
  - Responsibility: expose supported languages and language resource keys so tests can verify localization parity.
- Modify: `tests/NetGuardian.Tests/Program.cs`
  - Responsibility: add focused tests for localization and settings cleanup using the existing console-test style.
- Modify: `src/NetGuardian.App/MainWindow.xaml`
  - Responsibility: apply small layout polish for status and tool surfaces without changing navigation or behavior.
- Modify: `src/NetGuardian.App/MainWindow.xaml.cs`
  - Responsibility: add concise empty-state/status messages and keep button-state feedback consistent for existing operations.
- Modify: `src/NetGuardian.Core/Localization/Localizer.cs`
  - Responsibility: add any new UI strings in both `zh-CN` and `en-US`.
- Modify: `.gitignore`
  - Responsibility: ignore one-off local XAML rewrite/query helper files if they are not part of the supported project workflow.
- Leave unmodified unless explicitly needed: `src/NetGuardian.Core/Connectivity/ConnectivityTester.cs`, `src/NetGuardian.Core/Wifi/*`, and recovery logic in `MainWindow.xaml.cs`.

---

### Task 1: Add Localization Key Parity Test

**Files:**
- Modify: `tests/NetGuardian.Tests/Program.cs`
- Modify: `src/NetGuardian.Core/Localization/Localizer.cs`

- [ ] **Step 1: Write the failing test**

In `tests/NetGuardian.Tests/Program.cs`, add the missing namespace:

```csharp
using NetGuardian.Core.Localization;
using System.Text.Json;
```

Add this test to the `tests` list after `("normalize website URL", NormalizeWebsiteUrl),`:

```csharp
("localization resources have matching keys", LocalizationResourcesHaveMatchingKeys),
```

Add this method near the other static test methods:

```csharp
static void LocalizationResourcesHaveMatchingKeys()
{
    var localizer = new Localizer();
    var languages = localizer.SupportedLanguages.ToList();
    AssertEqual(2, languages.Count);
    AssertEqual(true, languages.Contains("zh-CN"));
    AssertEqual(true, languages.Contains("en-US"));

    var zhKeys = localizer.KeysForLanguage("zh-CN").OrderBy(key => key).ToList();
    var enKeys = localizer.KeysForLanguage("en-US").OrderBy(key => key).ToList();

    var missingInEnglish = zhKeys.Except(enKeys, StringComparer.OrdinalIgnoreCase).ToList();
    var missingInChinese = enKeys.Except(zhKeys, StringComparer.OrdinalIgnoreCase).ToList();

    if (missingInEnglish.Count > 0 || missingInChinese.Count > 0)
    {
        throw new InvalidOperationException(
            $"Localization keys differ. Missing in English: {string.Join(", ", missingInEnglish)}. Missing in Chinese: {string.Join(", ", missingInChinese)}.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
$root = Get-Location
. .\scripts\resolve-dotnet.ps1
$dotnet = Resolve-DotNetSdk
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet run --project tests\NetGuardian.Tests\NetGuardian.Tests.csproj --no-restore
```

Expected: compile failure because `Localizer` does not yet expose `SupportedLanguages` or `KeysForLanguage`.

- [ ] **Step 3: Add minimal production API**

In `src/NetGuardian.Core/Localization/Localizer.cs`, add these members after the `Language` property:

```csharp
public IReadOnlyCollection<string> SupportedLanguages => _resources.Keys.ToArray();

public IReadOnlyCollection<string> KeysForLanguage(string language)
{
    return _resources.TryGetValue(language, out var resources)
        ? resources.Keys.ToArray()
        : Array.Empty<string>();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run the same test command from Step 2.

Expected: existing tests pass and output includes:

```text
PASS localization resources have matching keys
```

- [ ] **Step 5: Commit**

```powershell
git add tests\NetGuardian.Tests\Program.cs src\NetGuardian.Core\Localization\Localizer.cs
git commit -m "test: verify localization key parity"
```

---

### Task 2: Strengthen Settings Cleanup Coverage

**Files:**
- Modify: `tests/NetGuardian.Tests/Program.cs`

- [ ] **Step 1: Write the failing test**

Add this test to the `tests` list after `("settings restore backup", SettingsRestoreBackup),`:

```csharp
("settings drop blank Wi-Fi profiles", SettingsDropBlankWifiProfiles),
```

Add this method near the existing settings tests:

```csharp
static void SettingsDropBlankWifiProfiles()
{
    var path = Path.Combine(Path.GetTempPath(), $"netguardian-{Guid.NewGuid():N}.json");
    try
    {
        var store = new SettingsStore(path);
        store.Save(new AppSettings
        {
            Profiles =
            [
                new WifiProfile { Ssid = "Office", Priority = 3 },
                new WifiProfile { Ssid = "   ", Priority = 1 },
                new WifiProfile { Ssid = "Backup", Priority = 2 }
            ]
        });

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var profiles = document.RootElement.GetProperty("profiles").EnumerateArray().ToList();
        AssertEqual(2, profiles.Count);
        AssertEqual("Backup", profiles[0].GetProperty("ssid").GetString());
        AssertEqual("Office", profiles[1].GetProperty("ssid").GetString());
    }
    finally
    {
        DeleteSettingsFiles(path);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
$root = Get-Location
. .\scripts\resolve-dotnet.ps1
$dotnet = Resolve-DotNetSdk
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet run --project tests\NetGuardian.Tests\NetGuardian.Tests.csproj --no-restore
```

Expected: `FAIL settings drop blank Wi-Fi profiles` because `SettingsStore.SaveCore` currently writes the blank profile into `settings.json`.

- [ ] **Step 3: Implement minimal cleanup in save path**

In `src/NetGuardian.Core/Settings/SettingsStore.cs`, replace this block in `SaveCore`:

```csharp
settings.Profiles = settings.Profiles
    .OrderBy(profile => profile.Priority)
    .ToList();
```

with:

```csharp
settings.Profiles = settings.Profiles
    .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid))
    .OrderBy(profile => profile.Priority)
    .ToList();
```

- [ ] **Step 4: Run test to verify it passes**

Run the same test command from Step 2.

Expected: output includes:

```text
PASS settings drop blank Wi-Fi profiles
```

- [ ] **Step 5: Commit**

```powershell
git add tests\NetGuardian.Tests\Program.cs src\NetGuardian.Core\Settings\SettingsStore.cs
git commit -m "fix: clean blank Wi-Fi profiles before saving"
```

---

### Task 3: Add Small UI Empty-State And Layout Polish

**Files:**
- Modify: `src/NetGuardian.Core/Localization/Localizer.cs`
- Modify: `src/NetGuardian.App/MainWindow.xaml`
- Modify: `src/NetGuardian.App/MainWindow.xaml.cs`
- Test: `tests/NetGuardian.Tests/Program.cs`

- [ ] **Step 1: Write the failing localization test**

In `tests/NetGuardian.Tests/Program.cs`, add this test to the `tests` list after `("localization resources have matching keys", LocalizationResourcesHaveMatchingKeys),`:

```csharp
("new UI polish strings are localized", NewUiPolishStringsAreLocalized),
```

Add this method near `LocalizationResourcesHaveMatchingKeys`:

```csharp
static void NewUiPolishStringsAreLocalized()
{
    var localizer = new Localizer();
    foreach (var language in new[] { "zh-CN", "en-US" })
    {
        localizer.SetLanguage(language);
        AssertNotKey("NoNearbyWifiRows", localizer.T("NoNearbyWifiRows"));
        AssertNotKey("NoPriorityWifiRows", localizer.T("NoPriorityWifiRows"));
        AssertNotKey("NoRecentEvents", localizer.T("NoRecentEvents"));
    }
}
```

Add this assertion helper after `AssertEqual<T>`:

```csharp
static void AssertNotKey(string key, string value)
{
    if (string.IsNullOrWhiteSpace(value) || value.Equals(key, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected localized value for {key}.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
$root = Get-Location
. .\scripts\resolve-dotnet.ps1
$dotnet = Resolve-DotNetSdk
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet run --project tests\NetGuardian.Tests\NetGuardian.Tests.csproj --no-restore
```

Expected: `FAIL new UI polish strings are localized` because the three keys are not yet present.

- [ ] **Step 3: Add localized UI strings**

In `src/NetGuardian.Core/Localization/Localizer.cs`, add these keys to the `zh-CN` dictionary near the Wi-Fi selection strings:

```csharp
["NoNearbyWifiRows"] = "尚未扫描到周围 Wi-Fi。",
["NoPriorityWifiRows"] = "恢复优先级列表为空。",
["NoRecentEvents"] = "暂无最近事件。",
```

Add matching keys to the `en-US` dictionary near the Wi-Fi selection strings:

```csharp
["NoNearbyWifiRows"] = "No nearby Wi-Fi networks scanned yet.",
["NoPriorityWifiRows"] = "The recovery priority list is empty.",
["NoRecentEvents"] = "No recent events yet.",
```

- [ ] **Step 4: Add UI empty-state elements**

In `src/NetGuardian.App/MainWindow.xaml`, add a `TextBlock` directly below `NearbyGrid` inside the same `DockPanel` that contains `NearbyNetworksTitle` and `NearbyGrid`:

```xml
<TextBlock x:Name="NearbyEmptyText" Margin="0,10,0,0" Foreground="#8FA0B5" TextWrapping="Wrap" Visibility="Collapsed" />
```

Add a `TextBlock` directly below `ProfileGrid` inside the same `DockPanel` that contains `PriorityListTitle` and `ProfileGrid`:

```xml
<TextBlock x:Name="ProfileEmptyText" Margin="0,10,0,0" Foreground="#8FA0B5" TextWrapping="Wrap" Visibility="Collapsed" />
```

Add a `TextBlock` directly below `RecentEventsList` inside the same `DockPanel` that contains `RecentEventsTitle` and `RecentEventsList`:

```xml
<TextBlock x:Name="RecentEventsEmptyText" Margin="0,10,0,0" Foreground="#8FA0B5" TextWrapping="Wrap" Visibility="Collapsed" />
```

- [ ] **Step 5: Wire empty-state text and visibility**

In `ApplyLanguage()`, after nearby/profile/recent-event titles are assigned, add:

```csharp
NearbyEmptyText.Text = _localizer.T("NoNearbyWifiRows");
ProfileEmptyText.Text = _localizer.T("NoPriorityWifiRows");
RecentEventsEmptyText.Text = _localizer.T("NoRecentEvents");
UpdateEmptyStates();
```

Add this method near other small UI update helpers:

```csharp
private void UpdateEmptyStates()
{
    NearbyEmptyText.Visibility = _nearbyNetworks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    ProfileEmptyText.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    RecentEventsEmptyText.Visibility = _recentEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
```

In `ScanWifiAsync`, replace this block:

```csharp
foreach (var network in networks)
{
    _nearbyNetworks.Add(CreateNearbyWifiRow(network));
}

RefreshNearbyPriorityMarkers();
```

with:

```csharp
foreach (var network in networks)
{
    _nearbyNetworks.Add(CreateNearbyWifiRow(network));
}

UpdateEmptyStates();
RefreshNearbyPriorityMarkers();
```

In `Remove_Click`, replace this block:

```csharp
_profiles.Remove(profile);
RenumberProfiles();
SaveSettings();
```

with:

```csharp
_profiles.Remove(profile);
RenumberProfiles();
UpdateEmptyStates();
SaveSettings();
```

In `AddWifiProfileAsync`, replace this block:

```csharp
RenumberProfiles();
SaveSettings();
```

with:

```csharp
RenumberProfiles();
UpdateEmptyStates();
SaveSettings();
```

In `Log`, replace this block:

```csharp
while (_recentEvents.Count > 6)
{
    _recentEvents.RemoveAt(_recentEvents.Count - 1);
}
```

with:

```csharp
while (_recentEvents.Count > 6)
{
    _recentEvents.RemoveAt(_recentEvents.Count - 1);
}

UpdateEmptyStates();
```

- [ ] **Step 6: Run tests and build**

Run:

```powershell
$root = Get-Location
. .\scripts\resolve-dotnet.ps1
$dotnet = Resolve-DotNetSdk
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet run --project tests\NetGuardian.Tests\NetGuardian.Tests.csproj --no-restore
.\scripts\build.ps1
```

Expected: tests pass and build reports 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add tests\NetGuardian.Tests\Program.cs src\NetGuardian.Core\Localization\Localizer.cs src\NetGuardian.App\MainWindow.xaml src\NetGuardian.App\MainWindow.xaml.cs
git commit -m "ui: add empty states for key panels"
```

---

### Task 4: Classify Local Helper Scripts

**Files:**
- Modify: `.gitignore`
- Inspect only: `scripts/Program.cs`
- Inspect only: `scripts/query_symbols.csproj`
- Inspect only: `scripts/rebuild_xaml.py`
- Inspect only: `scripts/rewrite_xaml.py`

- [ ] **Step 1: Inspect the untracked scripts**

Run:

```powershell
Get-Content -Raw scripts\Program.cs
Get-Content -Raw scripts\query_symbols.csproj
Get-Content -Raw scripts\rebuild_xaml.py
Get-Content -Raw scripts\rewrite_xaml.py
```

Expected: `Program.cs` and `query_symbols.csproj` are local WPF-UI symbol query utilities; `rebuild_xaml.py` and `rewrite_xaml.py` are one-off XAML rewrite scripts.

- [ ] **Step 2: Ignore local helper script debris**

Append these lines to `.gitignore`:

```gitignore
scripts/Program.cs
scripts/query_symbols.csproj
scripts/rebuild_xaml.py
scripts/rewrite_xaml.py
```

- [ ] **Step 3: Verify status no longer shows those files**

Run:

```powershell
git status --short --branch
```

Expected: the four script paths no longer appear as untracked files.

- [ ] **Step 4: Commit**

```powershell
git add .gitignore
git commit -m "chore: ignore local helper scripts"
```

---

### Task 5: Final Verification

**Files:**
- Inspect: all modified files

- [ ] **Step 1: Run all tests**

Run:

```powershell
$root = Get-Location
. .\scripts\resolve-dotnet.ps1
$dotnet = Resolve-DotNetSdk
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet run --project tests\NetGuardian.Tests\NetGuardian.Tests.csproj --no-restore
```

Expected: every test line begins with `PASS`.

- [ ] **Step 2: Run solution build**

Run:

```powershell
.\scripts\build.ps1
```

Expected: build completes with:

```text
0 个警告
0 个错误
```

- [ ] **Step 3: Inspect final diff**

Run:

```powershell
git status --short --branch
git diff --stat
```

Expected: no unexpected untracked files; diff is empty if all task commits succeeded.

- [ ] **Step 4: Optional app launch check**

If elevation and desktop launch are feasible, run:

```powershell
.\scripts\publish.ps1
```

Then start `dist\NetGuardian\NetGuardian.exe` manually or with the existing approved launch workflow. Confirm the app opens and the Home, Wi-Fi, and Tools surfaces render without obvious overlap.

If app launch is blocked by elevation, desktop session, or environment constraints, record the limitation in the final response.
