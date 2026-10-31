using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.Infrastructure.Station;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Data;

public static class DatabaseInitializer
{
    public static async Task InicializarAsync(AppDbContext context)
    {
        using var installationInitialization = InstallationWriteAuthorizationScope.Begin();
        await context.Database.MigrateAsync();

        if (!await context.Categorias.AnyAsync(c => c.Nombre == "Sin categoria"))
        {
            context.Categorias.Add(new Categoria
            {
                Nombre = "Sin categoria",
                Descripcion = "Categoria predeterminada para productos importados sin categoria.",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            });
        }

        await UpsertConfiguracionAsync(
            context,
            "caja.apertura_automatica",
            "false",
            "Indica si la caja se abre automaticamente al iniciar el sistema.");

        await UpsertConfiguracionAsync(
            context,
            "caja.monto_inicial_centavos",
            "0",
            "Monto inicial predeterminado para nuevas cajas.");

        await UpsertConfiguracionAsync(
            context,
            "sistema.version_base_datos",
            "1",
            "Version funcional inicial de la base de datos ArteInventariado V1.");

        foreach (var definition in CreditConfigurationKeys.Definitions)
        {
            await UpsertConfiguracionAsync(
                context,
                definition.Key,
                definition.DefaultValue,
                definition.Description);
        }

        await EnsureEmployeePermissionsAsync(context);

        await context.SaveChangesAsync();
    }

    private static async Task UpsertConfiguracionAsync(
        AppDbContext context,
        string clave,
        string valor,
        string descripcion)
    {
        var configuracion = await context.ConfiguracionSistema
            .FirstOrDefaultAsync(c => c.Clave == clave);

        if (configuracion is not null)
        {
            return;
        }

        context.ConfiguracionSistema.Add(new ConfiguracionSistema
        {
            Clave = clave,
            Valor = valor,
            Descripcion = descripcion,
            FechaModificacion = DateTime.UtcNow
        });
    }

    private static async Task EnsureEmployeePermissionsAsync(AppDbContext context)
    {
        var employeeIds = await context.Usuarios
            .Where(u => u.Rol == RolUsuario.EMPLEADO)
            .Select(u => u.UsuarioId)
            .ToListAsync();

        if (employeeIds.Count == 0)
        {
            return;
        }

        var existing = await context.UsuarioPermisos
            .Where(p => employeeIds.Contains(p.UsuarioId))
            .Select(p => new { p.UsuarioId, p.Permiso })
            .ToListAsync();
        var existingSet = existing
            .Select(p => (p.UsuarioId, p.Permiso))
            .ToHashSet();
        var now = DateTime.UtcNow;

        foreach (var employeeId in employeeIds)
        {
            foreach (var permiso in EmployeePermissionCatalog.AllPermissions)
            {
                if (existingSet.Contains((employeeId, permiso)))
                {
                    continue;
                }

                context.UsuarioPermisos.Add(new UsuarioPermiso
                {
                    UsuarioId = employeeId,
                    Permiso = permiso,
                    Habilitado = EmployeePermissionCatalog.RecommendedExistingEmployeePermissions.Contains(permiso),
                    FechaActualizacion = now
                });
            }
        }
    }
}
