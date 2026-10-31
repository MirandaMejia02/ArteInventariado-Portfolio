using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddEmployeeLoginActivityB1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_login_events",
                columns: table => new
                {
                    employee_login_event_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    logged_in_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    installation_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    station_mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_login_events", x => x.employee_login_event_id);
                    table.ForeignKey(
                        name: "FK_employee_login_events_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_login_events_usuario_id_logged_in_at_utc",
                table: "employee_login_events",
                columns: new[] { "usuario_id", "logged_in_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_login_events");
        }
    }
}
