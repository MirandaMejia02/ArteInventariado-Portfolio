namespace ArteInventariado.Application.Models;

public sealed record PrincipalProvisioningRequest(
    string Password,
    string ConfirmPassword);
