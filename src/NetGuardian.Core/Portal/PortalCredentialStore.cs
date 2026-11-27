using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetGuardian.Core.Models;
using NetGuardian.Core.Runtime;

namespace NetGuardian.Core.Portal;

public sealed record PortalCredential(string Username, string Password)
{
    public override string ToString() => "PortalCredential(***)";
}

public interface IPortalCredentialStore
{
    void Save(string credentialId, PortalCredential credential);
    bool TryLoad(string credentialId, out PortalCredential? credential);
    void Delete(string credentialId);
    bool Exists(string credentialId);
}

public sealed class InMemoryPortalCredentialStore : IPortalCredentialStore
{
    private readonly ConcurrentDictionary<string, PortalCredential> _items = new(StringComparer.OrdinalIgnoreCase);

    public void Save(string credentialId, PortalCredential credential)
    {
        if (!PortalAuthConfig.IsValidCredentialId(credentialId))
            throw new ArgumentException("无效的凭据标识符。", nameof(credentialId));
        _items[credentialId.ToLowerInvariant()] = credential;
    }

    public bool TryLoad(string credentialId, out PortalCredential? credential)
    {
        credential = null;
        if (!PortalAuthConfig.IsValidCredentialId(credentialId)) return false;
        return _items.TryGetValue(credentialId.ToLowerInvariant(), out credential);
    }

    public void Delete(string credentialId)
    {
        if (PortalAuthConfig.IsValidCredentialId(credentialId))
            _items.TryRemove(credentialId.ToLowerInvariant(), out _);
    }

    public bool Exists(string credentialId) =>
        PortalAuthConfig.IsValidCredentialId(credentialId) && _items.ContainsKey(credentialId.ToLowerInvariant());
}

/// <summary>
/// 使用 Windows DPAPI 加密保存网页认证凭据；机器目录下采用本机作用域与 SYSTEM/管理员受限 ACL，
/// 使得无人值守后台服务在无用户登录时也可安全读取凭据。
/// </summary>
public sealed class DpapiPortalCredentialStore : IPortalCredentialStore
{
    private const uint CryptProtectUiForbidden = 0x01;
    private const uint CryptProtectLocalMachine = 0x04;
    private const string RestrictedSddl = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";
    private static readonly byte[] ApplicationEntropyPrefix = "NetGuardian.PortalCredential.v1:"u8.ToArray();

    private readonly string _directory;
    private readonly bool _useMachineScope;
    private readonly bool _enforceStrictAcl;

    public DpapiPortalCredentialStore(string baseDirectory, bool useMachineScope)
    {
        _directory = Path.Combine(Path.GetFullPath(baseDirectory), "portal-credentials");
        _useMachineScope = useMachineScope;
        _enforceStrictAcl = useMachineScope && IsUnderMachineDirectory(baseDirectory);
    }

    public string DirectoryPath => _directory;

    public void Save(string credentialId, PortalCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("网页认证凭据加密存储仅支持 Windows DPAPI。");

        var normalizedId = ValidateAndNormalizeId(credentialId);
        Directory.CreateDirectory(_directory);
        if (_enforceStrictAcl) TryApplyRestrictedAcl(_directory, strict: true);

        var payload = new StoredPayload(credential.Username ?? "", credential.Password ?? "");
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var entropy = BuildEntropy(normalizedId);
        byte[] protectedBytes;
        try
        {
            protectedBytes = ProtectWindows(plainBytes, entropy, _useMachineScope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }

        var targetPath = FilePathFor(normalizedId);
        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, protectedBytes);
            if (_enforceStrictAcl) TryApplyRestrictedAcl(tempPath, strict: true);
            File.Move(tempPath, targetPath, overwrite: true);
            if (_enforceStrictAcl) TryApplyRestrictedAcl(targetPath, strict: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public bool TryLoad(string credentialId, out PortalCredential? credential)
    {
        credential = null;
        if (!OperatingSystem.IsWindows() || !PortalAuthConfig.IsValidCredentialId(credentialId)) return false;

        var normalizedId = credentialId.Trim().ToLowerInvariant();
        var path = FilePathFor(normalizedId);
        if (!File.Exists(path)) return false;

        byte[]? plainBytes = null;
        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            if (protectedBytes.Length == 0) return false;
            plainBytes = UnprotectWindows(protectedBytes, BuildEntropy(normalizedId));
            var payload = JsonSerializer.Deserialize<StoredPayload>(plainBytes);
            if (payload is null) return false;
            credential = new PortalCredential(payload.Username ?? "", payload.Password ?? "");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return false;
        }
        finally
        {
            if (plainBytes is not null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public void Delete(string credentialId)
    {
        if (!PortalAuthConfig.IsValidCredentialId(credentialId)) return;
        var path = FilePathFor(credentialId.Trim().ToLowerInvariant());
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public bool Exists(string credentialId) =>
        PortalAuthConfig.IsValidCredentialId(credentialId) && File.Exists(FilePathFor(credentialId.Trim().ToLowerInvariant()));

    private string FilePathFor(string normalizedId) => Path.Combine(_directory, normalizedId + ".bin");

    private static string ValidateAndNormalizeId(string credentialId)
    {
        if (!PortalAuthConfig.IsValidCredentialId(credentialId))
            throw new ArgumentException("无效的凭据标识符。", nameof(credentialId));
        return credentialId.Trim().ToLowerInvariant();
    }

    private static byte[] BuildEntropy(string normalizedId)
    {
        var idBytes = Encoding.ASCII.GetBytes(normalizedId);
        var entropy = new byte[ApplicationEntropyPrefix.Length + idBytes.Length];
        ApplicationEntropyPrefix.CopyTo(entropy, 0);
        idBytes.CopyTo(entropy, ApplicationEntropyPrefix.Length);
        return entropy;
    }

    private static bool IsUnderMachineDirectory(string baseDirectory)
    {
        var fullBase = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var machine = Path.GetFullPath(GuardianPaths.MachineDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullBase, machine, StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] plainBytes, byte[] entropy, bool machineScope)
    {
        var inHandle = GCHandle.Alloc(plainBytes, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        var outBlob = default(DataBlob);
        try
        {
            var inBlob = new DataBlob { cbData = plainBytes.Length, pbData = inHandle.AddrOfPinnedObject() };
            var entropyBlob = new DataBlob { cbData = entropy.Length, pbData = entropyHandle.AddrOfPinnedObject() };
            var flags = CryptProtectUiForbidden | (machineScope ? CryptProtectLocalMachine : 0);
            if (!NativeMethods.CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, flags, out outBlob))
                throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            inHandle.Free();
            entropyHandle.Free();
            if (outBlob.pbData != IntPtr.Zero) NativeMethods.LocalFree(outBlob.pbData);
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] protectedBytes, byte[] entropy)
    {
        var inHandle = GCHandle.Alloc(protectedBytes, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        var outBlob = default(DataBlob);
        try
        {
            var inBlob = new DataBlob { cbData = protectedBytes.Length, pbData = inHandle.AddrOfPinnedObject() };
            var entropyBlob = new DataBlob { cbData = entropy.Length, pbData = entropyHandle.AddrOfPinnedObject() };
            if (!NativeMethods.CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outBlob))
                throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            inHandle.Free();
            entropyHandle.Free();
            if (outBlob.pbData != IntPtr.Zero)
            {
                for (var i = 0; i < outBlob.cbData; i++) Marshal.WriteByte(outBlob.pbData, i, 0);
                NativeMethods.LocalFree(outBlob.pbData);
            }
        }
    }

    private static void TryApplyRestrictedAcl(string path, bool strict)
    {
        if (!OperatingSystem.IsWindows()) return;
        var sd = IntPtr.Zero;
        try
        {
            if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptorW(RestrictedSddl, 1, out sd, out _))
            {
                if (strict) throw new UnauthorizedAccessException("无法构造受限访问控制列表。");
                return;
            }
            if (!NativeMethods.GetSecurityDescriptorDacl(sd, out var present, out var dacl, out _) || !present)
            {
                if (strict) throw new UnauthorizedAccessException("无法读取受限访问控制列表。");
                return;
            }
            const int seFileObject = 1;
            const uint daclSecurityInfo = 0x00000004;
            const uint protectedDaclSecurityInfo = 0x80000000;
            var status = NativeMethods.SetNamedSecurityInfoW(path, seFileObject, daclSecurityInfo | protectedDaclSecurityInfo,
                IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
            if (status != 0 && strict)
                throw new UnauthorizedAccessException($"设置凭据保护权限失败（错误码 {status}）。");
        }
        finally
        {
            if (sd != IntPtr.Zero) NativeMethods.LocalFree(sd);
        }
    }

    private sealed record StoredPayload(string Username, string Password);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    private static class NativeMethods
    {
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CryptProtectData(
            ref DataBlob pDataIn,
            string? szDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            out DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CryptUnprotectData(
            ref DataBlob pDataIn,
            IntPtr ppszDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            out DataBlob pDataOut);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
            string StringSecurityDescriptor,
            uint StringSDRevision,
            out IntPtr SecurityDescriptor,
            out UIntPtr SecurityDescriptorSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSecurityDescriptorDacl(
            IntPtr pSecurityDescriptor,
            [MarshalAs(UnmanagedType.Bool)] out bool lpbDaclPresent,
            out IntPtr pDacl,
            [MarshalAs(UnmanagedType.Bool)] out bool lpbDaclDefaulted);

        [DllImport("advapi32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
        internal static extern uint SetNamedSecurityInfoW(
            string pObjectName,
            int ObjectType,
            uint SecurityInfo,
            IntPtr psidOwner,
            IntPtr psidGroup,
            IntPtr pDacl,
            IntPtr pSacl);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr LocalFree(IntPtr hMem);
    }
}
