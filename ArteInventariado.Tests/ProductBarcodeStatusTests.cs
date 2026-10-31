using ArteInventariado.Application.Models;

namespace ArteInventariado.Tests;

public sealed class ProductBarcodeStatusTests
{
    [Fact]
    public void SimpleWithActiveBarcode_IsComplete()
    {
        Assert.Equal(EstadoBarcodeProducto.Completo,
            ProductBarcodeStatusResolver.Resolve([(true, true)]));
    }

    [Fact]
    public void SimpleWithoutActiveBarcode_IsWithoutBarcode()
    {
        Assert.Equal(EstadoBarcodeProducto.SinBarcode,
            ProductBarcodeStatusResolver.Resolve([(true, false)]));
    }

    [Fact]
    public void VariantsWithEveryActiveSkuCovered_AreComplete()
    {
        Assert.Equal(EstadoBarcodeProducto.Completo,
            ProductBarcodeStatusResolver.Resolve([(true, true), (true, true), (false, false)]));
    }

    [Fact]
    public void VariantsWithSomeActiveSkusCovered_ArePending()
    {
        Assert.Equal(EstadoBarcodeProducto.Pendiente,
            ProductBarcodeStatusResolver.Resolve([(true, true), (true, false)]));
    }

    [Fact]
    public void VariantsWithNoActiveSkuCovered_AreWithoutBarcode()
    {
        Assert.Equal(EstadoBarcodeProducto.SinBarcode,
            ProductBarcodeStatusResolver.Resolve([(true, false), (true, false)]));
    }

    [Fact]
    public void InactiveSkuAndInactiveBarcode_DoNotCount()
    {
        Assert.Equal(EstadoBarcodeProducto.SinBarcode,
            ProductBarcodeStatusResolver.Resolve([(true, false), (false, true)]));
    }

    [Fact]
    public void AllFilterValue_IsNeutral()
    {
        Assert.Equal(EstadoBarcodeProducto.Todos, (EstadoBarcodeProducto)0);
    }
}
