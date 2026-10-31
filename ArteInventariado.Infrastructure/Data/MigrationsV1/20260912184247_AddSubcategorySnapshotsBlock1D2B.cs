using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddSubcategorySnapshotsBlock1D2B : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "subcategoria_id_snapshot",
                table: "detalle_ventas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subcategoria_nombre_snapshot",
                table: "detalle_ventas",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "subcategoria_id_snapshot",
                table: "detalle_cambios_venta",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subcategoria_nombre_snapshot",
                table: "detalle_cambios_venta",
                type: "TEXT",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subcategoria_id_snapshot",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "subcategoria_nombre_snapshot",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "subcategoria_id_snapshot",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "subcategoria_nombre_snapshot",
                table: "detalle_cambios_venta");
        }
    }
}
