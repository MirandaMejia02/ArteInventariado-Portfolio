using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddEmployeePermissionsV150 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuario_permisos",
                columns: table => new
                {
                    usuario_permiso_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    permiso = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    habilitado = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuario_permisos", x => x.usuario_permiso_id);
                    table.ForeignKey(
                        name: "FK_usuario_permisos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_usuario_permisos_permiso",
                table: "usuario_permisos",
                column: "permiso");

            migrationBuilder.CreateIndex(
                name: "IX_usuario_permisos_usuario_id_permiso",
                table: "usuario_permisos",
                columns: new[] { "usuario_id", "permiso" },
                unique: true);

            SeedExistingEmployeePermissions(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usuario_permisos");
        }

        private static void SeedExistingEmployeePermissions(MigrationBuilder migrationBuilder)
        {
            var enabledPermissions = new HashSet<string>(StringComparer.Ordinal)
            {
                "POS_ACCEDER",
                "VENTA_REGISTRAR",
                "DESCUENTO_LINEA_APLICAR",
                "DESCUENTO_GENERAL_APLICAR",
                "PAGO_TRANSFERENCIA_USAR",
                "PAGO_BITCOIN_USAR",
                "CORRECCIONES_ACCEDER",
                "DEVOLUCION_REGISTRAR",
                "CAMBIO_REGISTRAR",
                "CAJA_CERRAR",
                "GASTO_REGISTRAR",
                "RETIRO_REGISTRAR",
                "CAJA_TOTAL_ESPERADO_VER",
                "CAJA_DIFERENCIAS_VER"
            };

            var allPermissions = new[]
            {
                "POS_ACCEDER",
                "VENTA_REGISTRAR",
                "DESCUENTO_LINEA_APLICAR",
                "DESCUENTO_GENERAL_APLICAR",
                "PAGO_TRANSFERENCIA_USAR",
                "PAGO_BITCOIN_USAR",
                "COMPROBANTE_REIMPRIMIR",
                "HISTORIAL_VENTAS_VER",
                "CORRECCIONES_ACCEDER",
                "DEVOLUCION_REGISTRAR",
                "CAMBIO_REGISTRAR",
                "HISTORIAL_DEVOLUCIONES_VER",
                "CLIENTES_ACCEDER",
                "CLIENTE_CREAR",
                "CLIENTE_EDITAR",
                "CLIENTE_HISTORIAL_VER",
                "CUENTAS_COBRAR_VER",
                "CREDITO_REGISTRAR",
                "ABONO_REGISTRAR",
                "ALERTAS_CREDITO_VER",
                "CAJA_ABRIR",
                "CAJA_CERRAR",
                "GASTO_REGISTRAR",
                "RETIRO_REGISTRAR",
                "CAJA_TOTAL_ESPERADO_VER",
                "CAJA_DIFERENCIAS_VER"
            };

            foreach (var permission in allPermissions)
            {
                var enabled = enabledPermissions.Contains(permission) ? 1 : 0;
                migrationBuilder.Sql($"""
                    INSERT INTO usuario_permisos (usuario_id, permiso, habilitado, fecha_actualizacion)
                    SELECT usuario_id, '{permission}', {enabled}, CURRENT_TIMESTAMP
                    FROM usuarios
                    WHERE rol = 'EMPLEADO';
                    """);
            }
        }
    }
}
