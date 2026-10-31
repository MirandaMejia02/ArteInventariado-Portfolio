namespace ArteInventariado.Application.Models;

public enum EstadoBarcodeProducto
{
    Todos = 0,
    Completo = 1,
    Pendiente = 2,
    SinBarcode = 3
}

public static class ProductBarcodeStatusResolver
{
    public static EstadoBarcodeProducto Resolve(
        IEnumerable<(bool ActiveSku, bool HasActiveBarcode)> skuStates)
    {
        ArgumentNullException.ThrowIfNull(skuStates);

        var activeStates = skuStates.Where(state => state.ActiveSku).ToList();
        if (activeStates.Count == 0 || activeStates.All(state => !state.HasActiveBarcode))
        {
            return EstadoBarcodeProducto.SinBarcode;
        }

        return activeStates.All(state => state.HasActiveBarcode)
            ? EstadoBarcodeProducto.Completo
            : EstadoBarcodeProducto.Pendiente;
    }
}
