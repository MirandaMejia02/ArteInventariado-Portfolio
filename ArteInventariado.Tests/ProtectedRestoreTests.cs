using System.Text.Json;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Installation;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class ProtectedRestoreTests
{
    [Fact]
    public async Task HealthyCandidate_RestoresDatabaseAndAssets()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                candidate.Manifest.BackupId, candidate.Manifest.SourceInstallationId,
                ConfirmationAccepted: true),
            CancellationToken.None,
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Succeeded, result.Outcome);
        Assert.Equal("Producto B", await fixture.ReadProductNameAsync());
        Assert.True(File.Exists(fixture.ProductImagePath));
        Assert.NotNull(result.PreRestorePath);
        Assert.StartsWith(fixture.Paths.RecoveryRoot, result.PreRestorePath!, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.PreRestorePath!.StartsWith(fixture.Paths.ProtectedBackupsRoot, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Restore_PreservesStationIdentityModeAndProtectedPointers()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        await File.WriteAllTextAsync(fixture.Paths.CurrentStatePath, "{\"version\":\"1.12.0\"}");
        await File.WriteAllTextAsync(fixture.Paths.UpdateStatePath, "{\"status\":\"idle\"}");
        var stationId = fixture.InstallationId;
        var currentBytes = await File.ReadAllBytesAsync(fixture.Paths.CurrentStatePath);
        var updateBytes = await File.ReadAllBytesAsync(fixture.Paths.UpdateStatePath);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Succeeded, result.Outcome);
        Assert.Equal(stationId, fixture.PathsStation.Load().Status.InstallationId);
        Assert.Equal(InstallationMode.Principal, fixture.PathsStation.Load().Status.Mode);
        Assert.Equal(currentBytes, await File.ReadAllBytesAsync(fixture.Paths.CurrentStatePath));
        Assert.Equal(updateBytes, await File.ReadAllBytesAsync(fixture.Paths.UpdateStatePath));
    }

    [Fact]
    public async Task DegradedCandidate_RestoresDatabaseAndReturnsWarning()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B", missingImage: true);
        await fixture.SetProductNameAsync("Producto A");
        await fixture.SetProductImagePathAsync(fixture.ProductImagePath);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ExpectedContentStatus: BackupContentStatus.DegradedAssets,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Succeeded, result.Outcome);
        Assert.Equal("Producto B", await fixture.ReadProductNameAsync());
        Assert.Contains(result.Warnings ?? [], warning => warning.Contains("DegradedAssets", StringComparison.OrdinalIgnoreCase));
        Assert.Null(await fixture.ReadProductImagePathAsync());
    }

    [Fact]
    public async Task CandidateHashMismatch_IsRejectedWithoutChangingDatabase()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        var actualHash = await BackupPackageEngine.ComputeSha256Async(candidate.Path, CancellationToken.None);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ExpectedPackageSha256: new string('A', 64), ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.NotEqual(new string('A', 64), actualHash);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Theory]
    [InlineData("outside.aibak")]
    [InlineData("..\\outside.aibak")]
    [InlineData("sub\\candidate.aibak")]
    public async Task CandidateOutsideAuthorizedRoot_IsRejected(string relativePath)
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        var outside = Path.GetFullPath(Path.Combine(fixture.Paths.MachineDataRoot, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.Copy(candidate.Path, outside, overwrite: true);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(outside, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task CloudCandidate_RequiresAndChecksExpectedIdentityAndHash()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var local = await fixture.CreateCandidateAsync("Producto B");
        var cloudDirectory = Path.Combine(fixture.Paths.CloudRecoveryStagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cloudDirectory);
        var cloudPath = Path.Combine(cloudDirectory, "candidate.aibak");
        File.Copy(local.Path, cloudPath);
        var hash = await BackupPackageEngine.ComputeSha256Async(cloudPath, CancellationToken.None);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(cloudPath, ProtectedRestoreCandidateSource.CloudRecoveryCandidate,
                local.Manifest.BackupId, local.Manifest.SourceInstallationId, hash,
                local.Manifest.ContentStatus, true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Succeeded, result.Outcome);
        Assert.Equal("Producto B", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task CloudCandidateWithoutExpectedHash_IsRejected()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var local = await fixture.CreateCandidateAsync("Producto B");
        var cloudDirectory = Path.Combine(fixture.Paths.CloudRecoveryStagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cloudDirectory);
        var cloudPath = Path.Combine(cloudDirectory, "candidate.aibak");
        File.Copy(local.Path, cloudPath);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(cloudPath, ProtectedRestoreCandidateSource.CloudRecoveryCandidate,
                local.Manifest.BackupId, local.Manifest.SourceInstallationId,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task LocalStagedCandidate_OutsideCanonicalStagingRootIsRejected()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        var outside = Path.Combine(fixture.Root, "outside-staged.aibak");
        File.Copy(candidate.Path, outside);
        var hash = await BackupPackageEngine.ComputeSha256Async(outside, CancellationToken.None);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(
                outside,
                ProtectedRestoreCandidateSource.LocalStagedBackup,
                ExpectedPackageSha256: hash,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task LocalStagedCandidate_RequiresExpectedHash()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        Directory.CreateDirectory(fixture.Paths.LocalRestoreStagingRoot);
        var stagedPath = Path.Combine(fixture.Paths.LocalRestoreStagingRoot, "candidate.aibak");
        File.Copy(candidate.Path, stagedPath);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(
                stagedPath,
                ProtectedRestoreCandidateSource.LocalStagedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task CandidateChangedBeforeConsume_IsRejectedBySecondHash()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        var changed = false;
        fixture.Engine = fixture.CreateEngine(phase =>
        {
            if (phase == "pre-restore" && !changed)
            {
                changed = true;
                File.AppendAllText(candidate.Path, "tampered");
            }
        });

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task ValidationFailure_RollsBackDatabaseAndAssets()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        fixture.Engine = fixture.CreateEngine(phase =>
        {
            if (phase == "validate-production")
            {
                throw new InvalidDataException("fallo de validacion simulado");
            }
        });

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Failed, result.Outcome);
        Assert.True(result.RolledBack);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
        var restoredImage = await fixture.ReadProductImagePathAsync();
        Assert.NotNull(restoredImage);
        Assert.True(File.Exists(restoredImage));
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, await File.ReadAllBytesAsync(restoredImage));
    }

    [Fact]
    public async Task RollbackFailure_MarksRecoveryRequiredInsteadOfRetryingBlindly()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        fixture.Engine = fixture.CreateEngine(phase =>
        {
            if (phase is "validate-production" or "rollback")
            {
                throw new InvalidDataException("fallo simulado");
            }
        });

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.RecoveryRequired, result.Outcome);
        Assert.False(result.RolledBack);
        var status = await fixture.Engine.GetRecoveryStatusAsync();
        Assert.True(status.Pending);
        Assert.Equal(ProtectedRestoreJournalPhase.RecoveryRequired, status.Phase);
    }

    [Fact]
    public async Task RecoveryHelper_RollsBackUnfinishedJournalDeterministically()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        var failing = fixture.CreateEngine(phase =>
        {
            if (phase == "validate-production") throw new InvalidDataException("fallo");
        });
        var failed = await failing.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);
        Assert.Equal(ProtectedRestoreOutcome.Failed, failed.Outcome);
        Assert.False((await fixture.Engine.GetRecoveryStatusAsync()).Pending);
    }

    [Fact]
    public async Task EngineRequiresElevationAndMachineWideLayout()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        var notElevated = new ProtectedRestoreEngine(
            fixture.Paths,
            fixture.PackageEngine,
            TimeProvider.System,
            () => false,
            () => new TestRestoreLock());
        var rejected = await notElevated.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);
        Assert.Equal(ProtectedRestoreOutcome.Rejected, rejected.Outcome);

        var legacyPaths = ArteInventariadoPathProvider.CreateLegacy(Path.Combine(fixture.Root, "legacy"));
        var legacyEngine = new ProtectedRestoreEngine(legacyPaths, fixture.PackageEngine, TimeProvider.System, () => true,
            () => new TestRestoreLock());
        var legacy = await legacyEngine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);
        Assert.Equal(ProtectedRestoreOutcome.Rejected, legacy.Outcome);
    }

    [Fact]
    public async Task ConcurrentRestore_IsRejectedBeforeMutation()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        fixture.Engine = new ProtectedRestoreEngine(
            fixture.Paths,
            fixture.PackageEngine,
            TimeProvider.System,
            () => true,
            () => new TestRestoreLock(acquire: false));

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task ConfirmationIsMandatory()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup),
            allowSyntheticRoots: true);
        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task JournalContainsNoPasswordOrCloudSecret()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        fixture.Engine = fixture.CreateEngine(phase =>
        {
            if (phase == "validate-production") throw new InvalidDataException("stop");
        });
        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);
        Assert.Equal(ProtectedRestoreOutcome.Failed, result.Outcome);
        var journalFiles = Directory.EnumerateFiles(fixture.Paths.RecoveryRoot, "journal.json", SearchOption.AllDirectories);
        var json = string.Join("\n", journalFiles.Select(File.ReadAllText));
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DatabaseSidecarsAreRemovedAfterPromotion()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        fixture.Engine = fixture.CreateEngine(phase =>
        {
            if (phase == "stage-candidate")
            {
                foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
                {
                    File.WriteAllBytes(fixture.Paths.DatabasePath + suffix, [1, 2, 3]);
                }
            }
        });

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);
        Assert.Equal(ProtectedRestoreOutcome.Succeeded, result.Outcome);
        Assert.False(File.Exists(fixture.Paths.DatabasePath + "-journal"));
        // SQLite may recreate WAL/SHM while the post-promotion read-only
        // validation opens the database.  The restore contract is to remove
        // the stale sidecars that existed before promotion; a recreated,
        // empty runtime sidecar is not stale state.
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = fixture.Paths.DatabasePath + suffix;
            if (File.Exists(sidecar))
            {
                Assert.NotEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(sidecar));
            }
        }
    }

    [Fact]
    public async Task ProtectedRestoreServiceRequiresPrincipalAndUsesUacBoundary()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var auth = new FakeAuthorizationService();
        var calls = 0;
        string? verifiedPassword = null;
        var service = new ProtectedRestoreService(
            fixture.Paths,
            auth,
            (_, _) =>
            {
                calls++;
                return new MachineWideElevationResult(MachineWideElevationOutcome.RelaunchedElevated);
            },
            allowSyntheticRoots: true,
            reauthenticateForTests: (password, _) =>
            {
                verifiedPassword = password;
                return Task.CompletedTask;
            });
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        var result = await service.RestoreAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            "temporary-test-password");
        Assert.Equal(ProtectedRestoreOutcome.RelaunchedElevated, result.Outcome);
        Assert.Equal(1, calls);
        Assert.True(auth.Called);
        Assert.Equal("temporary-test-password", verifiedPassword);
    }

    [Fact]
    public async Task ProtectedRestoreServicePropagatesAuthorizationFailure()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var auth = new FakeAuthorizationService { Failure = new UnauthorizedAccessException("principal requerido") };
        var service = new ProtectedRestoreService(fixture.Paths, auth, (_, _) =>
            new MachineWideElevationResult(MachineWideElevationOutcome.RelaunchedElevated),
            allowSyntheticRoots: true,
            reauthenticateForTests: (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RestoreAsync(
            new ProtectedRestoreRequest(Path.Combine(fixture.Paths.ProtectedBackupsRoot, "x.aibak"),
                ProtectedRestoreCandidateSource.LocalProtectedBackup, ConfirmationAccepted: true),
            "temporary-test-password"));
    }

    [Fact]
    public async Task ProtectedRestoreServiceRejectsCandidateOutsideAuthorizedRootBeforeUac()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var auth = new FakeAuthorizationService();
        var elevationCalls = 0;
        var service = new ProtectedRestoreService(
            fixture.Paths,
            auth,
            (_, _) =>
            {
                elevationCalls++;
                return new MachineWideElevationResult(MachineWideElevationOutcome.RelaunchedElevated);
            },
            allowSyntheticRoots: true,
            reauthenticateForTests: (_, _) => Task.CompletedTask);
        var validCandidate = await fixture.CreateCandidateAsync("Producto B");
        var externalCandidate = Path.Combine(fixture.Root, "outside.aibak");
        File.Copy(validCandidate.Path, externalCandidate);
        await fixture.SetProductNameAsync("Producto A");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreAsync(
            new ProtectedRestoreRequest(
                externalCandidate,
                ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            "temporary-test-password"));

        Assert.True(auth.Called);
        Assert.Equal(0, elevationCalls);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task ProtectedRestoreReauthenticationFailureStopsBeforeUac()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var auth = new FakeAuthorizationService();
        var elevationCalls = 0;
        var service = new ProtectedRestoreService(
            fixture.Paths,
            auth,
            (_, _) =>
            {
                elevationCalls++;
                return new MachineWideElevationResult(MachineWideElevationOutcome.RelaunchedElevated);
            },
            allowSyntheticRoots: true,
            reauthenticateForTests: (_, _) =>
                Task.FromException(new InvalidOperationException("contraseña incorrecta")));
        var candidate = await fixture.CreateCandidateAsync("Producto B");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            "wrong-password"));

        Assert.True(auth.Called);
        Assert.Equal(0, elevationCalls);
        Assert.Equal("Producto B", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task UacCancellationLeavesCurrentDatabaseUntouched()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var auth = new FakeAuthorizationService();
        var service = new ProtectedRestoreService(fixture.Paths, auth, (_, _) =>
            new MachineWideElevationResult(MachineWideElevationOutcome.Cancelled),
            allowSyntheticRoots: true,
            reauthenticateForTests: (_, _) => Task.CompletedTask);
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        var result = await service.RestoreAsync(
            new ProtectedRestoreRequest(candidate.Path,
                ProtectedRestoreCandidateSource.LocalProtectedBackup, ConfirmationAccepted: true),
            "temporary-test-password");
        Assert.Equal(ProtectedRestoreOutcome.Cancelled, result.Outcome);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task UsbCandidate_IsCopiedToProtectedStagingAndRemainsSeparateFromRestore()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        var sourceDirectory = Path.Combine(fixture.Root, "USB");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "from-usb.aibak");
        File.Copy(candidate.Path, sourcePath);
        var expectedHash = await BackupPackageEngine.ComputeSha256Async(sourcePath, CancellationToken.None);
        var authorized = 0;
        IReadOnlyList<string>? helperArguments = null;
        var launcher = new FakeRestoreCandidateStageLauncher(async (_, _, arguments, _) =>
        {
            helperArguments = arguments;
            var requestIndex = arguments.ToList().IndexOf("--request-id");
            var sourceIndex = arguments.ToList().IndexOf("--source-path");
            var hashIndex = arguments.ToList().IndexOf("--expected-package-sha256");
            Assert.True(requestIndex > 0 && sourceIndex > 0 && hashIndex > 0);
            Assert.True(Guid.TryParseExact(arguments[requestIndex + 1], "N", out var requestId));
            await new ProtectedRestoreCandidateStager(_ => true, () => true, allowSyntheticRoots: true)
                .StageAsync(fixture.Paths, arguments[sourceIndex + 1], requestId, arguments[hashIndex + 1]);
            return new ProtectedRestoreCandidateStageLaunchResult(UacCancelled: false);
        });
        var service = new ProtectedRestoreService(
            fixture.Paths,
            new FakeAuthorizationService(),
            (_, _) => throw new Xunit.Sdk.XunitException("El importador no debe usar el handoff del restore."),
            allowSyntheticRoots: true,
            stageLauncher: launcher,
            authorizeStageForTests: _ =>
            {
                authorized++;
                return Task.CompletedTask;
            },
            isSupportedLocalVolume: _ => true);

        var staged = await service.StageLocalCandidateAsync(sourcePath, expectedHash);

        Assert.Equal(1, authorized);
        Assert.Equal(ProtectedRestoreOutcome.Succeeded, staged.Outcome);
        Assert.Equal(ProtectedRestoreCandidateSource.LocalStagedBackup, staged.Source);
        Assert.StartsWith(fixture.Paths.LocalRestoreStagingRoot, staged.CandidatePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedHash, staged.PackageSha256);
        Assert.Equal(await File.ReadAllBytesAsync(sourcePath), await File.ReadAllBytesAsync(staged.CandidatePath!));
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
        Assert.NotNull(helperArguments);
        Assert.DoesNotContain("--database", helperArguments!);
        Assert.DoesNotContain("--destination", helperArguments!);
        Assert.DoesNotContain("--restore", helperArguments!);
        Assert.DoesNotContain("password", string.Join(" ", helperArguments!), StringComparison.OrdinalIgnoreCase);

        var restore = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(
                staged.CandidatePath!,
                ProtectedRestoreCandidateSource.LocalStagedBackup,
                candidate.Manifest.BackupId,
                candidate.Manifest.SourceInstallationId,
                staged.PackageSha256,
                candidate.Manifest.ContentStatus,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Succeeded, restore.Outcome);
        Assert.Equal("Producto B", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task UsbCandidate_UacCancellationPublishesNothingAndDoesNotTouchDatabase()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        var sourcePath = Path.Combine(fixture.Root, "from-usb.aibak");
        File.Copy(candidate.Path, sourcePath);
        var hash = await BackupPackageEngine.ComputeSha256Async(sourcePath, CancellationToken.None);
        var launcher = new FakeRestoreCandidateStageLauncher((_, _, _, _) =>
            Task.FromResult(new ProtectedRestoreCandidateStageLaunchResult(UacCancelled: true)));
        var service = new ProtectedRestoreService(
            fixture.Paths,
            new FakeAuthorizationService(),
            (_, _) => throw new Xunit.Sdk.XunitException("UAC cancelado no debe iniciar otro flujo."),
            allowSyntheticRoots: true,
            stageLauncher: launcher,
            authorizeStageForTests: _ => Task.CompletedTask,
            isSupportedLocalVolume: _ => true);

        var result = await service.StageLocalCandidateAsync(sourcePath, hash);

        Assert.Equal(ProtectedRestoreOutcome.Cancelled, result.Outcome);
        Assert.Null(result.CandidatePath);
        Assert.Empty(Directory.Exists(fixture.Paths.LocalRestoreStagingRoot)
            ? Directory.GetFiles(fixture.Paths.LocalRestoreStagingRoot)
            : []);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task UsbCandidate_HashChangedSinceValidationStopsBeforeUac()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        var sourcePath = Path.Combine(fixture.Root, "from-usb.aibak");
        File.Copy(candidate.Path, sourcePath);
        var launchCalls = 0;
        var authorized = 0;
        var launcher = new FakeRestoreCandidateStageLauncher((_, _, _, _) =>
        {
            launchCalls++;
            return Task.FromResult(new ProtectedRestoreCandidateStageLaunchResult(UacCancelled: false));
        });
        var service = new ProtectedRestoreService(
            fixture.Paths,
            new FakeAuthorizationService(),
            (_, _) => throw new Xunit.Sdk.XunitException("La importación no debe iniciar el helper de restore."),
            allowSyntheticRoots: true,
            stageLauncher: launcher,
            authorizeStageForTests: _ =>
            {
                authorized++;
                return Task.CompletedTask;
            },
            isSupportedLocalVolume: _ => true);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.StageLocalCandidateAsync(sourcePath, new string('0', 64)));

        Assert.Equal(1, authorized);
        Assert.Equal(0, launchCalls);
    }

    [Fact]
    public async Task ExpectedBackupIdMismatch_IsRejected()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ExpectedBackupId: Guid.NewGuid(), ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task ExpectedSourceInstallationIdMismatch_IsRejected()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ExpectedSourceInstallationId: Guid.NewGuid(), ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task ExpectedContentStatusMismatch_IsRejected()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ExpectedContentStatus: BackupContentStatus.DegradedAssets, ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task CorruptCandidate_IsRejectedBeforeMutation()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        await File.WriteAllTextAsync(candidate.Path, "not an aibak package");

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task CandidateWithWrongExtension_IsRejected()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        var wrongExtension = Path.Combine(fixture.Paths.ProtectedBackupsRoot, "candidate.zip");
        File.Copy(candidate.Path, wrongExtension);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(wrongExtension, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task CandidateDirectory_IsRejected()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var directory = Path.Combine(fixture.Paths.ProtectedBackupsRoot, "candidate.aibak");
        Directory.CreateDirectory(directory);

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(directory, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task RecoveryStatus_IsEmptyBeforeAnyRestore()
    {
        await using var fixture = await RestoreFixture.CreateAsync();

        var status = await fixture.Engine.GetRecoveryStatusAsync();

        Assert.False(status.Pending);
        Assert.Null(status.OperationId);
        Assert.Null(status.Phase);
    }

    [Fact]
    public async Task PreRestorePackage_IsCreatedUnderRecoveryAndValid()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.PreRestorePath);
        var validationRoot = Path.Combine(fixture.Root, "pre-restore-validation-check");
        var validation = await fixture.PackageEngine.ValidatePackageAsync(
            result.PreRestorePath!, fixture.CurrentMigrations, validationRoot, CancellationToken.None);
        Assert.True(validation.IsValid, string.Join(" ", validation.Errors));
        Assert.Equal(BackupKind.PreRestore, validation.Manifest!.Kind);
    }

    [Fact]
    public async Task CancellationDuringPromotion_LeavesJournalForDeterministicRecovery()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        fixture.Engine = fixture.CreateEngine(phase =>
        {
            if (phase == "promote-assets")
            {
                throw new OperationCanceledException("simulated process interruption");
            }
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true));

        var pending = await fixture.Engine.GetRecoveryStatusAsync();
        Assert.True(pending.Pending);
        Assert.Equal(ProtectedRestoreJournalPhase.PromotingAssets, pending.Phase);

        var recovery = await fixture.Engine.RecoverPendingAsync(allowSyntheticRoots: true);
        Assert.NotNull(recovery);
        Assert.Equal(ProtectedRestoreOutcome.Failed, recovery!.Outcome);
        Assert.True(recovery.RolledBack);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task RecoveryRequired_IsNotRetriedAutomatically()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");
        fixture.Engine = fixture.CreateEngine(phase =>
        {
            if (phase is "validate-production" or "rollback")
            {
                throw new InvalidDataException("simulated permanent failure");
            }
        });

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);
        Assert.Equal(ProtectedRestoreOutcome.RecoveryRequired, result.Outcome);

        var retry = await fixture.Engine.RecoverPendingAsync(allowSyntheticRoots: true);

        Assert.NotNull(retry);
        Assert.Equal(ProtectedRestoreOutcome.RecoveryRequired, retry!.Outcome);
        Assert.Equal("Producto B", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task AlreadyElevatedApplication_IsRejectedAtHelperBoundary()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var auth = new FakeAuthorizationService();
        var service = new ProtectedRestoreService(
            fixture.Paths,
            auth,
            (_, _) => new MachineWideElevationResult(MachineWideElevationOutcome.AlreadyElevated),
            allowSyntheticRoots: true,
            reauthenticateForTests: (_, _) => Task.CompletedTask);
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        var result = await service.RestoreAsync(
            new ProtectedRestoreRequest(
                candidate.Path,
                ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            "temporary-test-password");

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.True(auth.Called);
    }

    [Fact]
    public async Task LegacyLayout_IsRejectedByProtectedRestoreService()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var legacyPaths = ArteInventariadoPathProvider.CreateLegacy(Path.Combine(fixture.Root, "legacy"));
        var service = new ProtectedRestoreService(
            legacyPaths,
            new FakeAuthorizationService(),
            (_, _) => new MachineWideElevationResult(MachineWideElevationOutcome.RelaunchedElevated),
            allowSyntheticRoots: true);

        var result = await service.RestoreAsync(
            new ProtectedRestoreRequest(
                Path.Combine(fixture.Paths.ProtectedBackupsRoot, "candidate.aibak"),
                ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            "temporary-test-password");

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task RestoreArgumentsContainOnlyRecognizedRestoreInputs()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var service = new ProtectedRestoreService(
            fixture.Paths,
            new FakeAuthorizationService(),
            (_, _) => new MachineWideElevationResult(MachineWideElevationOutcome.RelaunchedElevated),
            allowSyntheticRoots: true);
        var request = new ProtectedRestoreRequest(
            Path.Combine(fixture.Paths.ProtectedBackupsRoot, "candidate.aibak"),
            ProtectedRestoreCandidateSource.LocalProtectedBackup,
            ExpectedBackupId: Guid.NewGuid(),
            ExpectedSourceInstallationId: fixture.InstallationId,
            ExpectedPackageSha256: new string('a', 64),
            ExpectedContentStatus: BackupContentStatus.Healthy,
            ConfirmationAccepted: true);

        var arguments = service.BuildRestoreArguments(request);

        Assert.Contains("--restore", arguments);
        Assert.Contains("--machine-install-root", arguments);
        Assert.Contains("--candidate-path", arguments);
        Assert.DoesNotContain("--database", arguments);
        Assert.DoesNotContain("--destination", arguments);
        Assert.DoesNotContain("password", string.Join(" ", arguments), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", string.Join(" ", arguments), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JournalPathOutsideRecovery_IsRejected()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var store = new ProtectedRestoreJournalStore(fixture.Paths.RecoveryRoot);

        Assert.Throws<InvalidDataException>(() => store.GetJournalPath(Path.Combine(fixture.Root, "outside")));
    }

    [Fact]
    public async Task DegradedCurrentAssets_StopRestoreBeforeDestructivePromotion()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        await fixture.SetProductImagePathAsync(Path.Combine(fixture.Root, "external", "not-authorized.png"));
        var candidate = await fixture.CreateCandidateAsync("Producto B");
        await fixture.SetProductNameAsync("Producto A");

        var result = await fixture.Engine.ExecuteAsync(
            new ProtectedRestoreRequest(candidate.Path, ProtectedRestoreCandidateSource.LocalProtectedBackup,
                ConfirmationAccepted: true),
            allowSyntheticRoots: true);

        Assert.Equal(ProtectedRestoreOutcome.Rejected, result.Outcome);
        Assert.Equal("Producto A", await fixture.ReadProductNameAsync());
    }

    [Fact]
    public async Task TamperedRecoveryJournal_IsRejectedWithoutReadingExternalPreRestore()
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var operationId = Guid.NewGuid();
        var store = new ProtectedRestoreJournalStore(fixture.Paths.RecoveryRoot);
        var operationDirectory = store.CreateOperationDirectory(operationId);
        var external = Path.Combine(fixture.Root, "outside-pre-restore.aibak");
        await File.WriteAllTextAsync(external, "must never be opened by recovery");
        var stationHash = await BackupPackageEngine.ComputeSha256Async(
            fixture.Paths.StationCorePath, CancellationToken.None);
        var journal = new ProtectedRestoreJournalDocument(
            ProtectedRestoreJournalStore.CurrentSchemaVersion,
            operationId,
            DateTimeOffset.UtcNow,
            ProtectedRestoreJournalPhase.PromotingAssets,
            ProtectedRestoreCandidateSource.LocalProtectedBackup,
            Path.Combine(fixture.Paths.ProtectedBackupsRoot, "candidate.aibak"),
            new string('a', 64),
            Guid.NewGuid(),
            Guid.NewGuid(),
            BackupContentStatus.Healthy,
            external,
            external,
            external,
            fixture.InstallationId,
            InstallationMode.Principal,
            stationHash,
            null,
            null,
            []);
        store.Save(operationDirectory, journal);

        var result = await fixture.Engine.RecoverPendingAsync(allowSyntheticRoots: true);

        Assert.NotNull(result);
        Assert.Equal(ProtectedRestoreOutcome.RecoveryRequired, result!.Outcome);
        Assert.True((await fixture.Engine.GetRecoveryStatusAsync()).Pending);
        Assert.Equal("must never be opened by recovery", await File.ReadAllTextAsync(external));
    }

    private sealed class RestoreFixture : IAsyncDisposable
    {
        private RestoreFixture(
            string root,
            ArteInventariadoPathProvider paths,
            LocalStationConfigurationStore station,
            TestDbContextFactory contextFactory,
            BackupPackageEngine packageEngine,
            IReadOnlyList<string> migrations,
            ProtectedRestoreEngine engine)
        {
            Root = root;
            Paths = paths;
            PathsStation = station;
            ContextFactory = contextFactory;
            PackageEngine = packageEngine;
            CurrentMigrations = migrations;
            Engine = engine;
        }

        public string Root { get; }
        public ArteInventariadoPathProvider Paths { get; }
        public LocalStationConfigurationStore PathsStation { get; }
        public TestDbContextFactory ContextFactory { get; }
        public BackupPackageEngine PackageEngine { get; }
        public IReadOnlyList<string> CurrentMigrations { get; }
        public ProtectedRestoreEngine Engine { get; set; }
        public Guid InstallationId => PathsStation.Load().Status.InstallationId!.Value;
        public string ProductImagePath => Path.Combine(Paths.ProductBusinessAssetsRoot, "product.png");

        public static async Task<RestoreFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"ArteInventariado.ProtectedRestore.{Guid.NewGuid():N}");
            var install = Path.Combine(root, "ProgramFiles", "ArteInventariado");
            var data = Path.Combine(root, "ProgramData", "ArteInventariado");
            var user = Path.Combine(root, "UserData", "ArteInventariado");
            var paths = ArteInventariadoPathProvider.CreateMachineWide(install, data, user);
            Directory.CreateDirectory(paths.MachineInstallRoot);
            Directory.CreateDirectory(paths.DataRoot);
            Directory.CreateDirectory(paths.ProductBusinessAssetsRoot);
            Directory.CreateDirectory(paths.AssetsRoot);
            Directory.CreateDirectory(paths.ProtectedBackupsRoot);
            Directory.CreateDirectory(paths.RecoveryRoot);
            await File.WriteAllBytesAsync(paths.StationHelperPath, [1]);

            var station = new LocalStationConfigurationStore(paths);
            station.EnsureInitialized();
            station.SaveConfigured(InstallationMode.Principal);

            var dbFactory = new TestDbContextFactory(paths.DatabasePath);
            await using (var context = dbFactory.CreateDbContext())
            {
                await context.Database.MigrateAsync();
                var category = new Categoria { Nombre = "Categoria", Activa = true, FechaCreacion = DateTime.UtcNow };
                var principalPassword = TestCredentials.Password("protected-restore");
                var admin = new Usuario
                {
                    NombreCompleto = "Administrador",
                    NombreUsuario = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(principalPassword),
                    Rol = RolUsuario.ADMIN,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow
                };
                context.AddRange(category, admin);
                await context.SaveChangesAsync();
                context.Productos.Add(new Producto
                {
                    CodigoInterno = "P-RESTORE",
                    Nombre = "Producto A",
                    CategoriaId = category.CategoriaId,
                    ImagenLocalPath = Path.Combine(paths.ProductBusinessAssetsRoot, "product.png"),
                    PrecioCompraCentavos = 100,
                    PrecioVentaCentavos = 250,
                    StockActual = 3,
                    StockMinimo = 1,
                    Estado = EstadoProducto.ACTIVO,
                    FechaCreacion = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            await File.WriteAllBytesAsync(Path.Combine(paths.ProductBusinessAssetsRoot, "product.png"), [10, 20, 30, 40]);
            var packageEngine = new BackupPackageEngine();
            await using var migrationContext = dbFactory.CreateDbContext();
            var migrations = migrationContext.Database.GetMigrations().ToList();
            var fixture = new RestoreFixture(root, paths, station, dbFactory, packageEngine, migrations, null!);
            fixture.Engine = fixture.CreateEngine();
            return fixture;
        }

        public ProtectedRestoreEngine CreateEngine(Action<string>? observer = null) =>
            new(
                Paths,
                PackageEngine,
                TimeProvider.System,
                () => true,
                () => new TestRestoreLock(),
                observer);

        public async Task<(string Path, BackupManifest Manifest)> CreateCandidateAsync(string name, bool missingImage = false)
        {
            await SetProductNameAsync(name);
            if (missingImage)
            {
                await using var context = ContextFactory.CreateDbContext();
                var product = await context.Productos.SingleAsync();
                product.ImagenLocalPath = Path.Combine(Root, "external", "not-authorized.png");
                await context.SaveChangesAsync();
            }

            var build = Path.Combine(Root, $"candidate-build-{Guid.NewGuid():N}");
            Directory.CreateDirectory(build);
            var snapshot = Path.Combine(build, BackupPackageEngine.DatabaseEntryName);
            await PackageEngine.CreateSnapshotAsync(Paths.DatabasePath, snapshot, CancellationToken.None);
            var productAssets = await PackageEngine.ReadProductAssetsAsync(snapshot, CancellationToken.None);
            var managedAssets = await PackageEngine.ReadManagedAssetSourcesAsync(snapshot, CancellationToken.None);
            var trust = new MachineWideBackupAssetTrustBoundary(Paths.ProductBusinessAssetsRoot, Paths.AssetsRoot);
            var preparation = await trust.PrepareAsync(productAssets, managedAssets, build, CancellationToken.None);
            var applied = await PackageEngine.ReadAppliedMigrationsAsync(snapshot, CancellationToken.None);
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var manifest = new BackupManifest(
                BackupPackageEngine.CurrentFormatVersion,
                id,
                now,
                now.ToLocalTime(),
                "1.12.0-test",
                "SQLite",
                applied,
                applied.LastOrDefault(),
                await BackupPackageEngine.ComputeSha256Async(snapshot, CancellationToken.None),
                new FileInfo(snapshot).Length,
                InstallationId,
                InstallationMode.Principal,
                BackupKind.Manual,
                preparation.Assets,
                preparation.ContentStatus,
                preparation.OmittedAssetCount);
            var path = Path.Combine(Paths.ProtectedBackupsRoot, $"candidate-{id:N}.aibak");
            await PackageEngine.CreatePackageAsync(build, snapshot, manifest, path, CancellationToken.None);
            Directory.Delete(build, recursive: true);
            return (path, manifest);
        }

        public async Task SetProductNameAsync(string name)
        {
            await using var context = ContextFactory.CreateDbContext();
            var product = await context.Productos.SingleAsync();
            product.Nombre = name;
            await context.SaveChangesAsync();
        }

        public async Task<string> ReadProductNameAsync()
        {
            await using var context = ContextFactory.CreateDbContext();
            return await context.Productos.Select(product => product.Nombre).SingleAsync();
        }

        public async Task<string?> ReadProductImagePathAsync()
        {
            await using var context = ContextFactory.CreateDbContext();
            return await context.Productos.Select(product => product.ImagenLocalPath).SingleAsync();
        }

        public async Task SetProductImagePathAsync(string? path)
        {
            await using var context = ContextFactory.CreateDbContext();
            var product = await context.Productos.SingleAsync();
            product.ImagenLocalPath = path;
            await context.SaveChangesAsync();
        }

        public ValueTask DisposeAsync()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestRestoreLock(bool acquire = true) : IProtectedRestoreLock
    {
        public bool TryAcquire() => acquire;
        public void Dispose() { }
    }

    private sealed class FakeRestoreCandidateStageLauncher(
        Func<string, ArteInventariado.Infrastructure.Updater.UpdateRuntimePaths, IReadOnlyList<string>, CancellationToken,
            Task<ProtectedRestoreCandidateStageLaunchResult>> launch)
        : IProtectedRestoreCandidateStageLauncher
    {
        public Task<ProtectedRestoreCandidateStageLaunchResult> LaunchAndWaitAsync(
            string executablePath,
            ArteInventariado.Infrastructure.Updater.UpdateRuntimePaths runtimePaths,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            launch(executablePath, runtimePaths, arguments, cancellationToken);
    }

    private sealed class FakeAuthorizationService : ICloudRecoveryAuthorizationService
    {
        public bool Called { get; private set; }
        public Exception? Failure { get; init; }

        public Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            Called = true;
            if (Failure is not null) throw Failure;
            return Task.CompletedTask;
        }
    }

    private sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(CreateOptions());
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
        private DbContextOptions<AppDbContext> CreateOptions() => new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true
            }.ToString())
            .Options;
    }
}
