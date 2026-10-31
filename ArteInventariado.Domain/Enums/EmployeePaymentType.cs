namespace ArteInventariado.Domain.Enums;

/// <summary>
/// Administrative employee-payment categories. These values are not payroll,
/// tax or statutory-benefit calculations.
/// </summary>
public enum EmployeePaymentType
{
    Daily = 1,
    Weekly = 2,
    Biweekly = 3,
    Monthly = 4,
    Extraordinary = 5,

    // Spanish aliases keep the domain convenient for existing UI conventions.
    Diario = Daily,
    Semanal = Weekly,
    Quincenal = Biweekly,
    Mensual = Monthly,
    Extraordinario = Extraordinary,
    DIARIO = Daily,
    SEMANAL = Weekly,
    QUINCENAL = Biweekly,
    MENSUAL = Monthly,
    EXTRAORDINARIO = Extraordinary
}
