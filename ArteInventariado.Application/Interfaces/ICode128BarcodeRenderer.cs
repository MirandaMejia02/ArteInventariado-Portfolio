using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

public interface ICode128BarcodeRenderer
{
    Code128BarcodeRender Render(string barcodeValue, ProductLabelProfile profile);
}
