using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class V110PosBarcodeScanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo_sku_snapshot",
                table: "detalle_ventas",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_variante_snapshot",
                table: "detalle_ventas",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "producto_sku_id",
                table: "detalle_ventas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "producto_sku_barcodes",
                columns: table => new
                {
                    producto_sku_barcode_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_sku_id = table.Column<int>(type: "INTEGER", nullable: false),
                    codigo = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "BINARY"),
                    activo = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    es_principal = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_producto_sku_barcodes", x => x.producto_sku_barcode_id);
                    table.CheckConstraint("CK_producto_sku_barcodes_codigo_valido", "length(codigo) BETWEEN 1 AND 128 AND codigo = trim(codigo)");
                    table.CheckConstraint("CK_producto_sku_barcodes_principal_activo", "es_principal = 0 OR activo = 1");
                    table.ForeignKey(
                        name: "FK_producto_sku_barcodes_producto_skus_producto_sku_id",
                        column: x => x.producto_sku_id,
                        principalTable: "producto_skus",
                        principalColumn: "producto_sku_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_detalle_ventas_producto_sku_id",
                table: "detalle_ventas",
                column: "producto_sku_id");

            migrationBuilder.CreateIndex(
                name: "IX_producto_sku_barcodes_codigo",
                table: "producto_sku_barcodes",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_producto_sku_barcodes_producto_sku_id",
                table: "producto_sku_barcodes",
                column: "producto_sku_id",
                unique: true,
                filter: "es_principal = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_detalle_ventas_producto_skus_producto_sku_id",
                table: "detalle_ventas",
                column: "producto_sku_id",
                principalTable: "producto_skus",
                principalColumn: "producto_sku_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_detalle_ventas_producto_skus_producto_sku_id",
                table: "detalle_ventas");

            migrationBuilder.DropTable(
                name: "producto_sku_barcodes");

            migrationBuilder.DropIndex(
                name: "IX_detalle_ventas_producto_sku_id",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "codigo_sku_snapshot",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "nombre_variante_snapshot",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "producto_sku_id",
                table: "detalle_ventas");
        }
    }
}
