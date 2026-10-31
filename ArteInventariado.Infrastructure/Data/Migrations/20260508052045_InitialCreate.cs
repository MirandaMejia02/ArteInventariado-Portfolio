using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "productos",
                columns: table => new
                {
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_producto = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    nombre = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true),
                    precio_compra_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    precio_venta_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    stock_actual = table.Column<int>(type: "INTEGER", nullable: false),
                    stock_minimo = table.Column<int>(type: "INTEGER", nullable: false),
                    imagen_principal_ruta = table.Column<string>(type: "TEXT", nullable: true),
                    activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productos", x => x.producto_id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre_completo = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    nombre_usuario = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    rol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ultimo_login = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.usuario_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_productos_codigo_producto",
                table: "productos",
                column: "codigo_producto",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_nombre_usuario",
                table: "usuarios",
                column: "nombre_usuario",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "productos");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
