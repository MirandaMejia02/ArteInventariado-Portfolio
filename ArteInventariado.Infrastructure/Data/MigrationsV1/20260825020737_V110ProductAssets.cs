using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class V110ProductAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "producto_assets",
                columns: table => new
                {
                    producto_asset_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    storage_key = table.Column<Guid>(type: "TEXT", nullable: false),
                    ruta_relativa = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    nombre_original = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    extension = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    width = table.Column<int>(type: "INTEGER", nullable: false),
                    height = table.Column<int>(type: "INTEGER", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    orden = table.Column<int>(type: "INTEGER", nullable: false),
                    es_principal = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_producto_assets", x => x.producto_asset_id);
                    table.CheckConstraint("CK_producto_assets_dimensions", "width > 0 AND height > 0");
                    table.CheckConstraint("CK_producto_assets_order", "orden >= 0");
                    table.CheckConstraint("CK_producto_assets_size", "size_bytes > 0");
                    table.ForeignKey(
                        name: "FK_producto_assets_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "producto_sku_assets",
                columns: table => new
                {
                    producto_sku_asset_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_sku_id = table.Column<int>(type: "INTEGER", nullable: false),
                    storage_key = table.Column<Guid>(type: "TEXT", nullable: false),
                    ruta_relativa = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    nombre_original = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    extension = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    width = table.Column<int>(type: "INTEGER", nullable: false),
                    height = table.Column<int>(type: "INTEGER", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    orden = table.Column<int>(type: "INTEGER", nullable: false),
                    es_principal = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_producto_sku_assets", x => x.producto_sku_asset_id);
                    table.CheckConstraint("CK_producto_sku_assets_dimensions", "width > 0 AND height > 0");
                    table.CheckConstraint("CK_producto_sku_assets_order", "orden >= 0");
                    table.CheckConstraint("CK_producto_sku_assets_size", "size_bytes > 0");
                    table.ForeignKey(
                        name: "FK_producto_sku_assets_producto_skus_producto_sku_id",
                        column: x => x.producto_sku_id,
                        principalTable: "producto_skus",
                        principalColumn: "producto_sku_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_producto_assets_producto_id",
                table: "producto_assets",
                column: "producto_id",
                unique: true,
                filter: "es_principal = 1");

            migrationBuilder.CreateIndex(
                name: "IX_producto_assets_producto_id_orden",
                table: "producto_assets",
                columns: new[] { "producto_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "IX_producto_assets_storage_key",
                table: "producto_assets",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_producto_sku_assets_producto_sku_id",
                table: "producto_sku_assets",
                column: "producto_sku_id",
                unique: true,
                filter: "es_principal = 1");

            migrationBuilder.CreateIndex(
                name: "IX_producto_sku_assets_producto_sku_id_orden",
                table: "producto_sku_assets",
                columns: new[] { "producto_sku_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "IX_producto_sku_assets_storage_key",
                table: "producto_sku_assets",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "producto_assets");

            migrationBuilder.DropTable(
                name: "producto_sku_assets");
        }
    }
}
