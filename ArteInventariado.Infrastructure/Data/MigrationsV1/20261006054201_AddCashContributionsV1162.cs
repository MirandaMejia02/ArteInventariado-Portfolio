using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddCashContributionsV1162 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aportes_caja",
                columns: table => new
                {
                    aporte_caja_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    fecha_aporte = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aportes_caja", x => x.aporte_caja_id);
                    table.CheckConstraint("CK_aportes_caja_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_aportes_caja_motivo_obligatorio", "length(trim(motivo)) > 0");
                    table.ForeignKey(
                        name: "FK_aportes_caja_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aportes_caja_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aportes_caja_caja_id",
                table: "aportes_caja",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_aportes_caja_fecha_aporte",
                table: "aportes_caja",
                column: "fecha_aporte");

            migrationBuilder.CreateIndex(
                name: "IX_aportes_caja_usuario_id",
                table: "aportes_caja",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aportes_caja");
        }
    }
}
