using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Tests;

public sealed class SaleDraftCoordinatorTests
{
    [Fact]
    public void CreatesAndSwitchesIndependentDrafts()
    {
        var coordinator = new SaleDraftCoordinator();
        var first = coordinator.ActiveDraft;
        var item = CreateSaleItem(stock: 1);
        first.Lines.Add(new SaleDraftLine(item));
        first.SelectedCustomerId = 10;
        first.SelectedCustomerName = "Cliente A";

        Assert.True(coordinator.TryCreateDraft(out var second));
        Assert.NotSame(first, second);
        Assert.Empty(second.Lines);
        Assert.Null(second.SelectedCustomerId);

        second.Lines.Add(new SaleDraftLine(item));
        second.SelectedCustomerId = 20;
        second.SelectedCustomerName = "Cliente B";

        Assert.True(coordinator.TryActivate(first.Id));
        Assert.Single(coordinator.ActiveDraft.Lines);
        Assert.Equal(10, coordinator.ActiveDraft.SelectedCustomerId);
        Assert.Equal("Cliente A", coordinator.ActiveDraft.SelectedCustomerName);

        Assert.True(coordinator.TryActivate(second.Id));
        Assert.Single(coordinator.ActiveDraft.Lines);
        Assert.Equal(20, coordinator.ActiveDraft.SelectedCustomerId);
        Assert.Equal("Cliente B", coordinator.ActiveDraft.SelectedCustomerName);
    }

    [Fact]
    public void DraftsDoNotReserveOrShareStock()
    {
        var coordinator = new SaleDraftCoordinator();
        var item = CreateSaleItem(stock: 1);
        coordinator.ActiveDraft.Lines.Add(new SaleDraftLine(item));

        Assert.True(coordinator.TryCreateDraft(out var second));
        second.Lines.Add(new SaleDraftLine(item));

        Assert.Equal(1, item.StockDisponibleExhibicion);
        Assert.Single(coordinator.Drafts[0].Lines);
        Assert.Single(coordinator.Drafts[1].Lines);
    }

    [Fact]
    public void CheckoutStateIsKeptPerDraft()
    {
        var coordinator = new SaleDraftCoordinator();
        var first = coordinator.ActiveDraft;
        first.Checkout.CurrentPaymentMethod = "TRANSFERENCIA";
        first.Checkout.AcceptedTransferCentavos = 1250;
        first.Checkout.AcceptedTransferReference = "REF-A";

        Assert.True(coordinator.TryCreateDraft(out var second));
        second.Checkout.CurrentPaymentMethod = "EFECTIVO";
        second.Checkout.CashReceivedCentavos = 2500;

        Assert.Equal("TRANSFERENCIA", first.Checkout.CurrentPaymentMethod);
        Assert.Equal(1250, first.Checkout.AcceptedTransferCentavos);
        Assert.Equal("REF-A", first.Checkout.AcceptedTransferReference);
        Assert.Equal("EFECTIVO", second.Checkout.CurrentPaymentMethod);
        Assert.Equal(2500, second.Checkout.CashReceivedCentavos);
    }

    [Fact]
    public void ClosingNonEmptyDraftRequiresConfirmation()
    {
        var coordinator = new SaleDraftCoordinator();
        coordinator.ActiveDraft.Lines.Add(new SaleDraftLine(CreateSaleItem(stock: 2)));
        Assert.True(coordinator.TryCreateDraft(out var second));

        Assert.False(coordinator.TryClose(coordinator.Drafts[0].Id, discardConfirmed: false));
        Assert.Equal(2, coordinator.Drafts.Count);

        Assert.True(coordinator.TryClose(coordinator.Drafts[0].Id, discardConfirmed: true));
        Assert.Single(coordinator.Drafts);
        Assert.Same(second, coordinator.ActiveDraft);
    }

    [Fact]
    public void ClosingEmptyDraftAndCreatingNewDraftAreBoundedToFive()
    {
        var coordinator = new SaleDraftCoordinator();
        for (var i = 0; i < 4; i++)
        {
            Assert.True(coordinator.TryCreateDraft(out _));
        }

        Assert.Equal(5, coordinator.Drafts.Count);
        Assert.False(coordinator.TryCreateDraft(out _));

        var emptyDraftId = coordinator.Drafts[1].Id;
        Assert.True(coordinator.TryClose(emptyDraftId, discardConfirmed: false));
        Assert.Equal(4, coordinator.Drafts.Count);
        Assert.True(coordinator.TryCreateDraft(out _));
        Assert.Equal(5, coordinator.Drafts.Count);
    }

    [Fact]
    public void ClosingLastDraftLeavesOneReusableEmptyDraft()
    {
        var coordinator = new SaleDraftCoordinator();
        var id = coordinator.ActiveDraft.Id;
        coordinator.ActiveDraft.Lines.Add(new SaleDraftLine(CreateSaleItem(stock: 1)));

        Assert.True(coordinator.TryClose(id, discardConfirmed: true));
        Assert.Single(coordinator.Drafts);
        Assert.Empty(coordinator.ActiveDraft.Lines);
        Assert.Null(coordinator.ActiveDraft.SelectedCustomerId);
    }

    [Fact]
    public void ResetAllDiscardsOnlyInMemoryDrafts()
    {
        var coordinator = new SaleDraftCoordinator();
        coordinator.ActiveDraft.Lines.Add(new SaleDraftLine(CreateSaleItem(stock: 1)));
        Assert.True(coordinator.TryCreateDraft(out _));

        coordinator.ResetAll();

        Assert.Single(coordinator.Drafts);
        Assert.Empty(coordinator.ActiveDraft.Lines);
        Assert.Equal("Consumidor final", coordinator.ActiveDraft.SelectedCustomerName);
    }

    [Fact]
    public void DraftCreationNormalizesTimestampToUtc()
    {
        var localOffset = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(-6));
        var draft = new SaleDraft("Venta de prueba", localOffset);

        Assert.Equal(localOffset.UtcDateTime, draft.CreatedAtUtc.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, draft.CreatedAtUtc.Offset);
    }

    private static PosSkuSaleItem CreateSaleItem(int stock)
    {
        return new PosSkuSaleItem(
            ProductoId: 1,
            ProductoSkuId: Random.Shared.Next(1, int.MaxValue),
            CodigoInterno: "PRD-DRAFT",
            CodigoSku: "SKU-DRAFT",
            ProductoNombre: "Producto draft",
            VarianteNombre: null,
            CategoriaId: 1,
            CategoriaNombre: "General",
            Modo: ModoProducto.SIMPLE,
            Estado: EstadoProducto.ACTIVO,
            SkuActivo: true,
            SkuPrincipal: true,
            PrecioVentaEfectivoCentavos: 1000,
            CostoEfectivoCentavos: 500,
            MinimoExhibicion: 0,
            StockFisicoExhibicion: stock,
            StockReservadoExhibicion: 0,
            StockFisicoBodega: 0,
            StockReservadoBodega: 0,
            StockFisicoSinAsignar: 0,
            StockReservadoSinAsignar: 0,
            ImagenOwnerKind: null,
            ImagenAssetId: null,
            ImagenRutaRelativa: null,
            ImagenLocalPath: null);
    }
}
