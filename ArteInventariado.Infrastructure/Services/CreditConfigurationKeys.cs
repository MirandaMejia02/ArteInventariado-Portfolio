namespace ArteInventariado.Infrastructure.Services;

internal static class CreditConfigurationKeys
{
    public const string DefaultLimitCentavos = "credito.limite_predeterminado_centavos";
    public const string DefaultMaxActiveAccounts = "credito.maximo_cuentas_activas";
    public const string DefaultTermDays = "credito.plazo_predeterminado_dias";
    public const string EmployeeMaxTermDays = "credito.plazo_maximo_empleado_dias";
    public const string AdminMaxTermDays = "credito.plazo_maximo_admin_dias";
    public const string DefaultInterestPercent = "credito.interes_predeterminado_porcentaje";
    public const string DaysPastDueForMoroso = "credito.dias_mora";
    public const string AllowCreditWithOutstandingBalance = "credito.permitir_saldo_vigente";
    public const string AlertUpcomingDueDays = "credito.alertas.proximo_vencimiento_dias";
    public const string AlertNoPaymentDays = "credito.alertas.sin_abono_dias";
    public const string AlertNearLimitPercent = "credito.alertas.limite_porcentaje";
    public const string AlertEnableDueToday = "credito.alertas.vence_hoy.habilitada";
    public const string AlertEnableUpcomingDue = "credito.alertas.proxima_vencer.habilitada";
    public const string AlertEnableOverdue = "credito.alertas.vencida.habilitada";
    public const string AlertEnableMoroso = "credito.alertas.morosa.habilitada";
    public const string AlertEnableNoRecentPayment = "credito.alertas.sin_abono_reciente.habilitada";
    public const string AlertEnableNearLimit = "credito.alertas.cerca_limite.habilitada";
    public const string AlertEnableLimitReached = "credito.alertas.limite_alcanzado.habilitada";

    public static IReadOnlyList<CreditConfigDefinition> Definitions { get; } =
    [
        new(DefaultLimitCentavos, "7500", "Limite predeterminado de credito para clientes nuevos."),
        new(DefaultMaxActiveAccounts, "3", "Maximo predeterminado de cuentas por cobrar activas."),
        new(DefaultTermDays, "7", "Plazo predeterminado de credito en dias."),
        new(EmployeeMaxTermDays, "7", "Plazo maximo que puede usar un empleado en una venta a credito."),
        new(AdminMaxTermDays, "90", "Plazo maximo administrativo inicial para politicas de credito."),
        new(DefaultInterestPercent, "0", "Interes simple predeterminado para credito."),
        new(DaysPastDueForMoroso, "7", "Dias vencidos para considerar una cuenta morosa."),
        new(AllowCreditWithOutstandingBalance, "true", "Permite crear nuevo credito con saldo vigente no vencido."),
        new(AlertUpcomingDueDays, "3", "Dias de anticipacion para alertas de vencimiento proximo."),
        new(AlertNoPaymentDays, "7", "Dias sin abono para alertar una cuenta abierta."),
        new(AlertNearLimitPercent, "80", "Porcentaje del limite para alertar proximidad."),
        new(AlertEnableDueToday, "true", "Habilita alertas de cuentas que vencen hoy."),
        new(AlertEnableUpcomingDue, "true", "Habilita alertas de cuentas proximas a vencer."),
        new(AlertEnableOverdue, "true", "Habilita alertas de cuentas vencidas."),
        new(AlertEnableMoroso, "true", "Habilita alertas de cuentas morosas."),
        new(AlertEnableNoRecentPayment, "true", "Habilita alertas de cuentas sin abonos recientes."),
        new(AlertEnableNearLimit, "true", "Habilita alertas de clientes cerca del limite."),
        new(AlertEnableLimitReached, "true", "Habilita alertas de clientes con limite alcanzado.")
    ];
}

internal sealed record CreditConfigDefinition(string Key, string DefaultValue, string Description);
