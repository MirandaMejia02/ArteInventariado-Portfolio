using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class RelaxVentaDiscountMotivoCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ventas_descuento_motivo_obligatorio",
                table: "ventas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ventas_descuento_motivo_obligatorio",
                table: "ventas",
                sql: "descuento_centavos = 0 OR length(trim(coalesce(descuento_motivo, ''))) > 0");
        }
    }
}
