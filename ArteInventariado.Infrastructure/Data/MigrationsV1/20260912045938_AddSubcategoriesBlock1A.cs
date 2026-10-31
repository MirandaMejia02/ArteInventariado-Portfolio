using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddSubcategoriesBlock1A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "subcategoria_id",
                table: "productos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subcategorias",
                columns: table => new
                {
                    subcategoria_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    categoria_id = table.Column<int>(type: "INTEGER", nullable: false),
                    nombre = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false, collation: "NOCASE"),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true),
                    activa = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_modificacion = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subcategorias", x => x.subcategoria_id);
                    table.ForeignKey(
                        name: "FK_subcategorias_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "categorias",
                        principalColumn: "categoria_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_productos_subcategoria_id",
                table: "productos",
                column: "subcategoria_id");

            migrationBuilder.CreateIndex(
                name: "IX_subcategorias_categoria_id",
                table: "subcategorias",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "IX_subcategorias_categoria_id_nombre",
                table: "subcategorias",
                columns: new[] { "categoria_id", "nombre" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_productos_subcategorias_subcategoria_id",
                table: "productos",
                column: "subcategoria_id",
                principalTable: "subcategorias",
                principalColumn: "subcategoria_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_productos_subcategorias_subcategoria_id",
                table: "productos");

            migrationBuilder.DropTable(
                name: "subcategorias");

            migrationBuilder.DropIndex(
                name: "IX_productos_subcategoria_id",
                table: "productos");

            migrationBuilder.DropColumn(
                name: "subcategoria_id",
                table: "productos");
        }
    }
}
