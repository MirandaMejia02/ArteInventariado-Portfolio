using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Tests;

public sealed class EmployeeActivityFoundationTests
{
    [Fact]
    public void ActivityModels_DoNotContainCredentials()
    {
        var names = typeof(EmployeeActivitySummary).GetProperties()
            .Concat(typeof(EmployeeLoginActivity).GetProperties())
            .Concat(typeof(EmployeeActivityDay).GetProperties())
            .Select(p => p.Name)
            .ToArray();
        Assert.DoesNotContain(names, n => n.Contains("password", StringComparison.OrdinalIgnoreCase) || n.Contains("hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmployeesPermission_IsCataloguedAndDistinct()
    {
        Assert.Equal(155, (int)PermisoEmpleado.EMPLEADOS_VER);
        var definition = EmployeePermissionCatalog.GetDefinition(PermisoEmpleado.EMPLEADOS_VER);
        Assert.Equal("Administracion", definition.Grupo);
        Assert.Contains("empleados", definition.Descripcion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoginEvent_ContainsOnlyOperationalActivityFields()
    {
        var names = typeof(EmployeeLoginEvent).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, n => n.Contains("password", StringComparison.OrdinalIgnoreCase) || n.Contains("hash", StringComparison.OrdinalIgnoreCase));
    }
}
