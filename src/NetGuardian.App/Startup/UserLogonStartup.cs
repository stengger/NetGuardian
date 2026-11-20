using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using NetGuardian.App.Updates;

namespace NetGuardian.App.Startup;

/// <summary>将桌面登录启动与 SYSTEM 开机守护分开管理。</summary>
public sealed class UserLogonStartup
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly ILogonTaskStore _store;
    private readonly string _executable;
    private readonly string _userId;
    private string TaskName => "NetGuardian-Logon-" + _userId;

    public UserLogonStartup() : this(new WindowsLogonTaskStore(),
        Path.Combine(AppContext.BaseDirectory, "NetGuardian.exe"), GetUserId()) { }

    internal UserLogonStartup(ILogonTaskStore store, string executable, string userId)
    {
        _store = store;
        _executable = Path.GetFullPath(executable);
        _userId = userId;
    }

    public bool IsEnabled()
    {
        var xml = _store.Read(TaskName);
        if (xml is null) return false;
        var task = XDocument.Parse(xml);
        return IsOwned(task)
            && task.Root!.Element(Ns + "Settings")?.Element(Ns + "Enabled")?.Value != "false"
            && task.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "Enabled")?.Value != "false";
    }

    public void SetEnabled(bool enabled)
    {
        var current = _store.Read(TaskName);
        if (current is not null && !IsOwned(XDocument.Parse(current)))
            throw new InvalidOperationException("已有登录启动任务的用户或程序路径不匹配，请先从原安装目录关闭登录启动。");
        if (enabled) _store.Write(TaskName, CreateDefinition().ToString(), _userId);
        else if (current is not null) _store.Delete(TaskName);
    }

    private bool IsOwned(XDocument task)
    {
        var principals = task.Root?.Element(Ns + "Principals")?.Elements().ToArray();
        var actions = task.Root?.Element(Ns + "Actions")?.Elements().ToArray();
        var triggers = task.Root?.Element(Ns + "Triggers")?.Elements().ToArray();
        return principals is { Length: 1 } && actions is { Length: 1 } && triggers is { Length: 1 }
            && principals[0].Name == Ns + "Principal"
            && principals[0].Element(Ns + "UserId")?.Value == _userId
            && principals[0].Element(Ns + "LogonType")?.Value == "InteractiveToken"
            && principals[0].Element(Ns + "RunLevel")?.Value == "HighestAvailable"
            && triggers[0].Name == Ns + "LogonTrigger"
            && triggers[0].Element(Ns + "UserId")?.Value == _userId
            && actions[0].Name == Ns + "Exec"
            && string.Equals(actions[0].Element(Ns + "Command")?.Value, _executable, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(actions[0].Element(Ns + "Arguments")?.Value);
    }

    private XDocument CreateDefinition() => new(new XElement(Ns + "Task", new XAttribute("version", "1.2"),
        new XElement(Ns + "RegistrationInfo", new XElement(Ns + "Description", "当前用户登录 Windows 后启动 NetGuardian 桌面界面。")),
        new XElement(Ns + "Triggers", new XElement(Ns + "LogonTrigger",
            new XElement(Ns + "Enabled", true), new XElement(Ns + "UserId", _userId))),
        new XElement(Ns + "Principals", new XElement(Ns + "Principal", new XAttribute("id", "CurrentUser"),
            new XElement(Ns + "UserId", _userId), new XElement(Ns + "LogonType", "InteractiveToken"),
            new XElement(Ns + "RunLevel", "HighestAvailable"))),
        new XElement(Ns + "Settings",
            new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
            new XElement(Ns + "DisallowStartIfOnBatteries", false), new XElement(Ns + "StopIfGoingOnBatteries", false),
            new XElement(Ns + "StartWhenAvailable", true), new XElement(Ns + "Enabled", true),
            new XElement(Ns + "ExecutionTimeLimit", "PT0S")),
        new XElement(Ns + "Actions", new XAttribute("Context", "CurrentUser"), new XElement(Ns + "Exec",
            new XElement(Ns + "Command", _executable), new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(_executable))))));

    private static string GetUserId()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("无法确定当前 Windows 用户。");
    }
}

internal interface ILogonTaskStore
{
    string? Read(string taskName);
    void Write(string taskName, string xml, string userId);
    void Delete(string taskName);
}

internal sealed class WindowsLogonTaskStore : ILogonTaskStore
{
    public string? Read(string taskName) => WithFolder<string?>(folder =>
    {
        dynamic? task = null;
        try
        {
            task = folder.GetTask(taskName);
            return (string)task.Xml;
        }
        // 动态 COM 可能把同一 HRESULT 映射为 FileNotFoundException。
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
        finally { Release(task); }
    });

    public void Write(string taskName, string xml, string userId)
    {
        var executable = XDocument.Parse(xml).Descendants("{http://schemas.microsoft.com/windows/2004/02/mit/task}Command").Single().Value;
        var directory = Path.GetDirectoryName(executable)!;
        if (!UpdateInstallLauncher.IsInstalledDirectory(directory))
            throw new InvalidOperationException("请从 Program Files 中已安装的 NetGuardian 开启登录启动。");
        UpdateStorage.AssertProtected(new FileInfo(executable));
        // 提升权限任务的目标及父目录均不能由普通用户替换。
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
        {
            UpdateStorage.AssertProtected(parent);
            if (string.Equals(parent.FullName, programFiles, StringComparison.OrdinalIgnoreCase)) break;
        }
        WithFolder(folder =>
        {
            // TASK_CREATE_OR_UPDATE=6；TASK_LOGON_INTERACTIVE_TOKEN=3，不存储密码。
            dynamic task = folder.RegisterTask(taskName, xml, 6, userId, null, 3, null);
            Release(task);
            return true;
        });
    }

    public void Delete(string taskName) => WithFolder(folder =>
    {
        folder.DeleteTask(taskName, 0);
        return true;
    });

    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        dynamic? scheduler = null;
        dynamic? folder = null;
        try
        {
            scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!);
            scheduler!.Connect();
            folder = scheduler.GetFolder(@"\");
            return action(folder);
        }
        finally
        {
            Release(folder);
            Release(scheduler);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
