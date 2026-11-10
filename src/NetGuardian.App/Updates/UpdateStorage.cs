using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NetGuardian.App.Updates;

internal sealed class UpdateStorage
{
    private static readonly string[] Trusted = { "S-1-5-18", "S-1-5-32-544", "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464" };
    public string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetGuardian", "updates");

    public string PrepareDownloads()
    {
        PrepareRoot();
        return CreateProtectedDirectory(Path.Combine(Root, "downloads"));
    }

    public string CreateJob()
    {
        PrepareRoot();
        CreateProtectedDirectory(Path.Combine(Root, "jobs"));
        return CreateProtectedDirectory(Path.Combine(Root, "jobs", Guid.NewGuid().ToString()));
    }

    private void PrepareRoot()
    {
        CreateProtectedDirectory(Path.GetDirectoryName(Root)!);
        CreateProtectedDirectory(Root);
    }

    private static string CreateProtectedDirectory(string path)
    {
        AssertNoReparsePoints(path);
        var directory = new DirectoryInfo(path);
        if (directory.Exists) AssertProtected(directory);
        else
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(new SecurityIdentifier(Trusted[1]));
            foreach (var sid in Trusted.Take(2))
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.Create(security);
            AssertProtected(directory);
        }
        return path;
    }

    public static void ProtectFile(string path)
    {
        AssertNoReparsePoints(path);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(new SecurityIdentifier(Trusted[1]));
        foreach (var sid in Trusted.Take(2))
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
        AssertProtected(new FileInfo(path));
    }

    internal static void AssertNoReparsePoints(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("更新路径不能包含重解析点。");
            current = Path.GetDirectoryName(current);
        }
    }

    internal static void AssertProtected(FileSystemInfo info)
    {
        AssertNoReparsePoints(info.FullName);
        FileSystemSecurity security = info is DirectoryInfo directory ? directory.GetAccessControl() : ((FileInfo)info).GetAccessControl();
        AssertSecurity(security);
    }

    internal static void AssertSecurity(FileSystemSecurity security)
    {
        if (!Trusted.Contains(security.GetOwner(typeof(SecurityIdentifier))?.Value ?? "")) throw new IOException("更新目录所有者不受信任。");
        const FileSystemRights write = FileSystemRights.Write | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | (FileSystemRights)0x50000000;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & write) != 0 && !Trusted.Contains(rule.IdentityReference.Value))
                throw new IOException("更新目录允许非管理员写入。");
        }
    }
}
