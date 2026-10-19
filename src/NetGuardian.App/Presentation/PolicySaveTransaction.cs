using System;
using System.Text.Json;
using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;

namespace NetGuardian.App.Presentation;

internal static class PolicySaveTransaction
{
    public static void Commit(AppSettings settings, MonitorPolicy candidate, Action<AppSettings> save)
    {
        // 文件写入可能失败；保存独立快照，成功后才发布给并行读取策略的引擎。
        var snapshot = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        snapshot.Policy = candidate;
        save(snapshot);
        settings.Policy = snapshot.Policy;
    }
}
