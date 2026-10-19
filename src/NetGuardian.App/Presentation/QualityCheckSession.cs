using System;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal enum QualityCheckPhase { Idle, Running, Cancelling, Complete, Cancelled, Failed }

internal sealed class QualityCheckSession
{
    private Guid _runId;
    public QualityCheckPhase Phase { get; private set; }
    public ConnectivityReport? Report { get; private set; }
    public string? Error { get; private set; }
    public bool IsBusy => Phase is QualityCheckPhase.Running or QualityCheckPhase.Cancelling;

    public Guid Begin()
    {
        if (IsBusy) throw new InvalidOperationException("连接检查尚未结束。");
        _runId = Guid.NewGuid();
        Phase = QualityCheckPhase.Running;
        Report = null;
        Error = null;
        return _runId;
    }

    public void RequestCancel()
    {
        if (Phase == QualityCheckPhase.Running) Phase = QualityCheckPhase.Cancelling;
    }

    public void Complete(Guid runId, ConnectivityReport report)
    {
        if (runId != _runId || Phase != QualityCheckPhase.Running) return;
        Report = report;
        Phase = QualityCheckPhase.Complete;
    }

    public void Cancel(Guid runId)
    {
        if (runId == _runId && IsBusy) Phase = QualityCheckPhase.Cancelled;
    }

    public void Fail(Guid runId, string error)
    {
        if (runId != _runId || !IsBusy) return;
        Error = error;
        Phase = QualityCheckPhase.Failed;
    }
}
