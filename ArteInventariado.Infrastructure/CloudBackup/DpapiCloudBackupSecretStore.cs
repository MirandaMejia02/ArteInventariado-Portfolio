using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Installation;

#pragma warning disable CA1416 // DPAPI and ACL calls are guarded by Windows/elevation checks.

namespace ArteInventariado.Infrastructure.CloudBackup;

public interface ICloudBackupSecretStore
{
    CloudBackupCredential? LoadOptional();

    void Save(CloudBackupCredential credential);
}

/// <summary>
/// Stores the upload-only credential encrypted with Windows DPAPI machine
/// scope.  The file ACL is reduced to SYSTEM and Administrators so DPAPI is
/// not treated as a substitute for filesystem authorization.
/// </summary>
public sealed class DpapiCloudBackupSecretStore : ICloudBackupSecretStore
{
    private const int CryptProtectLocalMachine = 0x4;
    private const int MaximumSecretBytes = 16 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private readonly ArteInventariadoPathProvider _paths;
    private readonly Func<bool> _isAdministratorOrSystem;
    private readonly Action<string>? _applyExclusiveAcl;

    public DpapiCloudBackupSecretStore(ArteInventariadoPathProvider paths)
        : this(paths, MachineWideElevation.IsAdministratorOrSystem, ApplyExclusiveAcl)
    {
    }

    internal DpapiCloudBackupSecretStore(
        ArteInventariadoPathProvider paths,
        Func<bool> isAdministratorOrSystem,
        Action<string>? applyExclusiveAcl)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _isAdministratorOrSystem = isAdministratorOrSystem ?? throw new ArgumentNullException(nameof(isAdministratorOrSystem));
        _applyExclusiveAcl = applyExclusiveAcl;
    }

    public CloudBackupCredential? LoadOptional()
    {
        EnsureMachineWide();
        EnsureNoReparse(_paths.CloudBackupCredentialsRoot);
        if (!File.Exists(_paths.CloudBackupCredentialPath))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(_paths.CloudBackupCredentialPath);
        if (encrypted.Length is <= 0 or > MaximumSecretBytes)
        {
            throw new InvalidDataException("La credencial cloud protegida tiene un tamaño inválido.");
        }

        var clear = Unprotect(encrypted);
        if (clear.Length is <= 0 or > MaximumSecretBytes)
        {
            throw new InvalidDataException("La credencial cloud descifrada tiene un tamaño inválido.");
        }

        try
        {
            var credential = JsonSerializer.Deserialize<CloudBackupCredential>(clear, SerializerOptions);
            Validate(credential);
            return credential;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("La credencial cloud protegida no es válida.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    public void Save(CloudBackupCredential credential)
    {
        EnsureMachineWide();
        if (!_isAdministratorOrSystem())
        {
            throw new UnauthorizedAccessException("Provisionar la credencial cloud requiere una operación administrativa.");
        }

        Validate(credential);
        Directory.CreateDirectory(_paths.CloudBackupCredentialsRoot);
        EnsureNoReparse(_paths.CloudBackupRoot);
        EnsureNoReparse(_paths.CloudBackupCredentialsRoot);
        _applyExclusiveAcl?.Invoke(_paths.CloudBackupCredentialsRoot);

        var clear = JsonSerializer.SerializeToUtf8Bytes(credential, SerializerOptions);
        if (clear.Length > MaximumSecretBytes)
        {
            CryptographicOperations.ZeroMemory(clear);
            throw new InvalidDataException("La credencial cloud excede el tamaño permitido.");
        }

        byte[] encrypted;
        try
        {
            encrypted = Protect(clear);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
        var temporary = Path.Combine(
            _paths.CloudBackupCredentialsRoot,
            $".s3-upload-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_paths.CloudBackupCredentialPath))
            {
                File.Replace(temporary, _paths.CloudBackupCredentialPath, null);
            }
            else
            {
                File.Move(temporary, _paths.CloudBackupCredentialPath);
            }

            _applyExclusiveAcl?.Invoke(_paths.CloudBackupCredentialPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            TryDelete(temporary);
        }
    }

    public static void Validate(CloudBackupCredential? credential)
    {
        if (credential is null ||
            string.IsNullOrWhiteSpace(credential.AccessKeyId) ||
            string.IsNullOrWhiteSpace(credential.SecretAccessKey) ||
            credential.AccessKeyId.Length > 256 ||
            credential.SecretAccessKey.Length > 1024)
        {
            throw new InvalidDataException("La credencial cloud no es válida.");
        }
    }

    private void EnsureMachineWide()
    {
        if (!_paths.IsMachineWide)
        {
            throw new InvalidOperationException("La credencial cloud requiere el layout machine-wide.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("La credencial cloud usa DPAPI de Windows.");
        }
    }

    private static byte[] Protect(byte[] clear)
    {
        var input = ToBlob(clear);
        var output = default(DataBlob);
        try
        {
            if (!CryptProtectData(
                    ref input,
                    "ArteInventariado S3 upload credential",
                    IntPtr.Zero,
                    null,
                    IntPtr.Zero,
                    CryptProtectLocalMachine,
                    ref output))
            {
                throw new InvalidOperationException($"DPAPI no pudo proteger la credencial (Win32 {Marshal.GetLastWin32Error()}).");
            }

            return FromBlob(output);
        }
        finally
        {
            FreeBlob(ref input);
            FreeBlob(ref output);
        }
    }

    private static byte[] Unprotect(byte[] encrypted)
    {
        var input = ToBlob(encrypted);
        var output = default(DataBlob);
        try
        {
            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    null,
                    IntPtr.Zero,
                    0,
                    ref output))
            {
                throw new InvalidOperationException($"DPAPI no pudo descifrar la credencial (Win32 {Marshal.GetLastWin32Error()}).");
            }

            return FromBlob(output);
        }
        finally
        {
            FreeBlob(ref input);
            FreeBlob(ref output);
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = pointer };
    }

    private static byte[] FromBlob(DataBlob blob)
    {
        if (blob.Size <= 0 || blob.Data == IntPtr.Zero || blob.Size > MaximumSecretBytes)
        {
            throw new InvalidDataException("DPAPI devolvió un blob inválido.");
        }

        var bytes = new byte[blob.Size];
        Marshal.Copy(blob.Data, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void FreeBlob(ref DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            if (blob.Size > 0)
            {
                var bytes = new byte[blob.Size];
                Marshal.Copy(blob.Data, bytes, 0, blob.Size);
                CryptographicOperations.ZeroMemory(bytes);
            }

            LocalFree(blob.Data);
            blob = default;
        }
    }

    private static void EnsureNoReparse(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"La ruta de la credencial contiene un reparse point: '{current}'.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }

    private static void ApplyExclusiveAcl(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("La ACL de la credencial cloud requiere Windows.");
        }

        var security = Directory.Exists(path)
            ? (FileSystemSecurity)new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var existing in security
                     .GetAccessRules(true, true, typeof(SecurityIdentifier))
                     .Cast<FileSystemAccessRule>()
                     .ToArray())
        {
            security.RemoveAccessRuleSpecific(existing);
        }

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var rules = new[] { system, administrators }
            .Select(identity => new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                Directory.Exists(path)
                    ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                    : InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
        foreach (var rule in rules)
        {
            security.AddAccessRule(rule);
        }

        if (security is DirectorySecurity directorySecurity)
        {
            new DirectoryInfo(path).SetAccessControl(directorySecurity);
        }
        else
        {
            new FileInfo(path).SetAccessControl((FileSecurity)security);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        string? reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("Crypt32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        string? reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("Kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}

public sealed class CloudBackupProvisioner
{
    private readonly Func<bool> _isAdministratorOrSystem;

    public CloudBackupProvisioner()
        : this(MachineWideElevation.IsAdministratorOrSystem)
    {
    }

    internal CloudBackupProvisioner(Func<bool> isAdministratorOrSystem)
    {
        _isAdministratorOrSystem = isAdministratorOrSystem;
    }

    public void Provision(
        ArteInventariadoPathProvider paths,
        CloudBackupConfiguration configuration,
        CloudBackupCredential credential)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!_isAdministratorOrSystem())
        {
            throw new UnauthorizedAccessException("Provisionar cloud requiere una operación administrativa.");
        }

        CloudBackupConfigurationStore.Validate(configuration);
        DpapiCloudBackupSecretStore.Validate(credential);
        new DpapiCloudBackupSecretStore(paths).Save(credential);
        new CloudBackupConfigurationStore(paths).Save(configuration);
    }
}

#pragma warning restore CA1416
