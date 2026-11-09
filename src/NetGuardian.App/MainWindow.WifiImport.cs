using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;

namespace NetGuardian.App;

public partial class MainWindow
{
    private Task? _savedWifiImportTask;

    private Task ImportSavedWifiProfilesAsync() => ImportSavedWifiProfilesAsync(CancellationToken.None);

    private async Task ImportSavedWifiProfilesAsync(CancellationToken cancellationToken)
    {
        if (_resourcesCleaned) return;
        if (_savedWifiImportTask is { IsCompleted: false })
        {
            await _savedWifiImportTask.WaitAsync(cancellationToken);
            return;
        }
        _savedWifiImportTask = ImportSavedWifiProfilesCoreAsync(cancellationToken);
        await _savedWifiImportTask;
    }

    private async Task ImportSavedWifiProfilesCoreAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        SavedWifiProfileInventory inventory;
        string? currentSsid;
        try
        {
            inventory = await _wifi.GetSavedProfilesAsync(deadline.Token);
            if (!inventory.Success)
            {
                Log(string.Format(_localizer.T("SavedWifiImportFailed"), inventory.Error), UiLogLevel.Warning);
                return;
            }
            currentSsid = await _wifi.GetCurrentSsidAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (!_resourcesCleaned) Log(_localizer.T("SavedWifiImportTimeout"), UiLogLevel.Warning);
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (_resourcesCleaned) return;

        // 先保存独立快照，再发布给界面和监控，避免写入失败仍显示导入成功。
        var (snapshot, result) = WifiProfileImportTransaction.PrepareAndSave(
            _settings, _profiles, inventory, currentSsid, _store.Save);
        if (!result.Changed) return;
        // 保留已有行的对象，避免正在编辑的弹窗持有被替换的网络记录。
        foreach (var imported in snapshot.Profiles.OrderBy(profile => profile.Priority))
        {
            var existing = _profiles.FirstOrDefault(profile => SsidEquals(profile.Ssid, imported.Ssid));
            if (existing is null) _profiles.Add(imported);
            else
            {
                existing.WindowsProfileName = imported.WindowsProfileName;
                existing.CredentialSaved = imported.CredentialSaved;
                existing.Security = imported.Security;
            }
        }
        _settings.Profiles = _profiles.ToList();
        ProfileGrid.Items.Refresh();
        UpdateEmptyStates();
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();
        UpdateFirstRunGuide();
        if (result.AddedCount > 0) Log(string.Format(_localizer.T("SavedWifiImported"), result.AddedCount));
    }

    private Task<bool> ConnectSavedWifiAsync(string ssid)
    {
        var profile = _profiles.FirstOrDefault(item => SsidEquals(item.Ssid, ssid));
        return profile is not null && profile.ConnectionProfileName != ssid
            ? _wifi.ConnectProfileAsync(profile.ConnectionProfileName, ssid)
            : _wifi.ConnectAsync(ssid);
    }
}
