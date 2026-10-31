using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Tests;

public sealed class ApplicationInstanceMutexTests
{
    [Fact]
    public void InteractiveMutexUsesANewNameAndPreservesTheLegacyNameForResetCoordination()
    {
        Assert.Equal(
            "Global\\ArteInventariado.WinForms.Interactive.v2",
            ApplicationInstanceMutex.InteractiveMutexName);
        Assert.Equal(
            "Global\\ArteInventariado.WinForms.Interactive",
            ApplicationInstanceMutex.LegacyInteractiveMutexName);
        Assert.NotEqual(
            ApplicationInstanceMutex.LegacyInteractiveMutexName,
            ApplicationInstanceMutex.InteractiveMutexName);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void InteractiveMutexSecurityGrantsStandardUsersRequiredRights()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var security = ApplicationInstanceMutex.CreateInteractiveSecurity();
        var rule = Assert.Single(security
            .GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(SecurityIdentifier))
            .Cast<MutexAccessRule>(),
            candidate => candidate.IdentityReference.Equals(usersSid));

        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(MutexRights.Synchronize | MutexRights.Modify, rule.MutexRights);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void InteractiveMutexFactoryCreatesAndReopensANamedMutex()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = $"{ApplicationInstanceMutex.InteractiveMutexName}.test.{Guid.NewGuid():N}";
        using var first = ApplicationInstanceMutex.CreateNamed(initiallyOwned: false, name, out var firstCreated);
        using var second = ApplicationInstanceMutex.CreateNamed(initiallyOwned: false, name, out var secondCreated);

        Assert.True(firstCreated);
        Assert.False(secondCreated);
    }
}
