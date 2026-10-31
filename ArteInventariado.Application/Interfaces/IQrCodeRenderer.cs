using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface IQrCodeRenderer
{
    Code128BarcodeRender Render(string payload, ProductLabelProfile profile);
}
