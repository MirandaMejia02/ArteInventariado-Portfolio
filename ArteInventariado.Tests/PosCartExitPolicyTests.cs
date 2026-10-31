using ArteInventariado.Application.Services;

namespace ArteInventariado.Tests;

public sealed class PosCartExitPolicyTests
{
    [Fact]
    public void EmptyCart_ExitsWithoutConfirmation()
    {
        Assert.False(PosCartExitPolicy.RequiresConfirmation(new(0, 0), false, false));
    }

    [Theory]
    [InlineData(PosExitReason.CloseWindow)]
    [InlineData(PosExitReason.ReturnToDashboard)]
    [InlineData(PosExitReason.Logout)]
    [InlineData(PosExitReason.ApplicationExit)]
    public void NonEmptyCart_RequiresOneConfirmationForEveryDestructiveExit(PosExitReason reason)
    {
        var summary = new PosCartExitSummary(3, 5);

        Assert.True(PosCartExitPolicy.RequiresConfirmation(summary, false, false));
        Assert.Contains("3 productos (5 unidades)", PosCartExitPolicy.BuildMessage(summary, reason));
    }

    [Fact]
    public void SingleProduct_UsesCorrectSingularSummary()
    {
        var message = PosCartExitPolicy.BuildMessage(new(1, 1), PosExitReason.CloseWindow);

        Assert.Contains("1 producto (1 unidad)", message);
    }

    [Fact]
    public void ConfirmedDiscard_DoesNotPromptTwice()
    {
        Assert.False(PosCartExitPolicy.RequiresConfirmation(new(3, 5), false, true));
    }

    [Fact]
    public void ControlledRestoreShutdown_DoesNotBlockApprovedRestart()
    {
        Assert.False(PosCartExitPolicy.RequiresConfirmation(new(3, 5), true, false));
    }

    [Fact]
    public void ContinueSale_LeavesCartIntactAndCancelsNavigation()
    {
        var cartUnits = 5;
        var prompts = 0;

        var allowed = PosCartExitPolicy.TryExit(
            new(3, cartUnits),
            false,
            false,
            () => { prompts++; return false; },
            () => cartUnits = 0);

        Assert.False(allowed);
        Assert.Equal(5, cartUnits);
        Assert.Equal(1, prompts);
    }

    [Fact]
    public void ExitAndDiscard_ClearsCartAndContinuesNavigation()
    {
        var cartUnits = 5;

        var allowed = PosCartExitPolicy.TryExit(
            new(3, cartUnits),
            false,
            false,
            () => true,
            () => cartUnits = 0);

        Assert.True(allowed);
        Assert.Equal(0, cartUnits);
    }

    [Fact]
    public void MultiplePendingDrafts_UseClearPluralWarning()
    {
        var message = PosCartExitPolicy.BuildMultiDraftMessage(
            draftCount: 2,
            summary: new PosCartExitSummary(2, 3),
            reason: PosExitReason.ApplicationExit);

        Assert.Contains("Hay 2 ventas pendientes", message);
        Assert.Contains("2 productos (3 unidades)", message);
        Assert.Contains("cerrar ArteInventariado", message);
    }
}
