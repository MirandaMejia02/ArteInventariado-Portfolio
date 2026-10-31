namespace ArteInventariado.Domain.Enums;

/// <summary>
/// Identifies where the money for a financial operation came from. This is
/// deliberately separate from <see cref="MetodoPago"/>: the same method can
/// be paid by the business or by an external owner contribution.
/// </summary>
public enum OrigenFondos
{
    CAJA_FISICA = 1,
    FONDOS_EXTERNOS_DUENO = 2,
    FONDOS_NEGOCIO_FUERA_CAJA = 3
}
