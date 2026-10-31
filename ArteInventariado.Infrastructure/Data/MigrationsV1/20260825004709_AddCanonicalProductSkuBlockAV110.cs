using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddCanonicalProductSkuBlockAV110 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "modo",
                table: "productos",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "SIMPLE");

            migrationBuilder.CreateTable(
                name: "producto_skus",
                columns: table => new
                {
                    producto_sku_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    codigo_sku = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    nombre_variante = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    activo = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    precio_venta_override_centavos = table.Column<long>(type: "INTEGER", nullable: true),
                    costo_override_centavos = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_producto_skus", x => x.producto_sku_id);
                    table.CheckConstraint("CK_producto_skus_codigo_obligatorio", "length(trim(codigo_sku)) > 0");
                    table.CheckConstraint("CK_producto_skus_costo_override_no_negativo", "costo_override_centavos IS NULL OR costo_override_centavos >= 0");
                    table.CheckConstraint("CK_producto_skus_nombre_variante_no_vacio", "nombre_variante IS NULL OR length(trim(nombre_variante)) > 0");
                    table.CheckConstraint("CK_producto_skus_precio_override_no_negativo", "precio_venta_override_centavos IS NULL OR precio_venta_override_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_producto_skus_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_producto_skus_codigo_sku",
                table: "producto_skus",
                column: "codigo_sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_producto_skus_producto_id",
                table: "producto_skus",
                column: "producto_id");

            migrationBuilder.Sql(
                """
                INSERT INTO producto_skus (
                    producto_id,
                    codigo_sku,
                    nombre_variante,
                    activo,
                    precio_venta_override_centavos,
                    costo_override_centavos)
                SELECT
                    producto_id,
                    codigo_interno,
                    NULL,
                    CASE WHEN estado = 'ACTIVO' THEN 1 ELSE 0 END,
                    NULL,
                    NULL
                FROM productos;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "producto_skus");

            migrationBuilder.DropColumn(
                name: "modo",
                table: "productos");
        }
    }
}
