using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddCustomersBlockBV150 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cliente_id",
                table: "ventas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cliente_nombre_snapshot",
                table: "ventas",
                type: "TEXT",
                maxLength: 180,
                nullable: false,
                defaultValue: "Consumidor final");

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    cliente_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_cliente = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    nombre_completo = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    nombre_normalizado = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    telefono_celular = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    telefono_normalizado = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    whatsapp = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    correo_electronico = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    correo_normalizado = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    direccion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    documento_identidad = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    documento_normalizado = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    notas = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    usuario_creacion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_actualizacion_id = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes", x => x.cliente_id);
                    table.ForeignKey(
                        name: "FK_clientes_usuarios_usuario_actualizacion_id",
                        column: x => x.usuario_actualizacion_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_clientes_usuarios_usuario_creacion_id",
                        column: x => x.usuario_creacion_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ventas_cliente_id",
                table: "ventas",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_codigo_cliente",
                table: "clientes",
                column: "codigo_cliente",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clientes_correo_normalizado",
                table: "clientes",
                column: "correo_normalizado");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_documento_normalizado",
                table: "clientes",
                column: "documento_normalizado",
                unique: true,
                filter: "documento_normalizado IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_estado",
                table: "clientes",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_nombre_normalizado",
                table: "clientes",
                column: "nombre_normalizado");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_telefono_normalizado",
                table: "clientes",
                column: "telefono_normalizado");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_usuario_actualizacion_id",
                table: "clientes",
                column: "usuario_actualizacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_usuario_creacion_id",
                table: "clientes",
                column: "usuario_creacion_id");

            migrationBuilder.AddForeignKey(
                name: "FK_ventas_clientes_cliente_id",
                table: "ventas",
                column: "cliente_id",
                principalTable: "clientes",
                principalColumn: "cliente_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ventas_clientes_cliente_id",
                table: "ventas");

            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropIndex(
                name: "IX_ventas_cliente_id",
                table: "ventas");

            migrationBuilder.DropColumn(
                name: "cliente_id",
                table: "ventas");

            migrationBuilder.DropColumn(
                name: "cliente_nombre_snapshot",
                table: "ventas");
        }
    }
}
