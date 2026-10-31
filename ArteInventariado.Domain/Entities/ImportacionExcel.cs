using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Domain.Entities;

public class ImportacionExcel
{
    public int ImportacionExcelId { get; set; }

    public string NombreArchivo { get; set; } = string.Empty;

    public string RutaArchivo { get; set; } = string.Empty;

    public int UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    public DateTime FechaImportacion { get; set; } = DateTime.UtcNow;

    public int FilasLeidas { get; set; }

    public int FilasImportadas { get; set; }

    public int FilasConError { get; set; }

    public EstadoImportacionExcel Estado { get; set; } = EstadoImportacionExcel.PENDIENTE;

    public string? ResumenErrores { get; set; }
}
