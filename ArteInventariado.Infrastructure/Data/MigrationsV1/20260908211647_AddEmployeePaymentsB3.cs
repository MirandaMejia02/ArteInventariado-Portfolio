using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddEmployeePaymentsB3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_payments",
                columns: table => new
                {
                    employee_payment_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    amount_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    payment_type = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    days_considered = table.Column<int>(type: "INTEGER", nullable: true),
                    note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    registered_by_user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    cancelled_by_user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    cancelled_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    cancellation_reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_payments", x => x.employee_payment_id);
                    table.CheckConstraint("CK_employee_payments_amount_positive", "amount_centavos > 0");
                    table.CheckConstraint("CK_employee_payments_cancelled_state", "status <> 'Cancelled' OR (cancelled_by_user_id IS NOT NULL AND cancelled_at_utc IS NOT NULL AND length(trim(coalesce(cancellation_reason, ''))) > 0)");
                    table.CheckConstraint("CK_employee_payments_days_non_negative", "days_considered IS NULL OR days_considered >= 0");
                    table.CheckConstraint("CK_employee_payments_period_order", "period_start IS NULL OR period_end IS NULL OR period_end >= period_start");
                    table.ForeignKey(
                        name: "FK_employee_payments_usuarios_cancelled_by_user_id",
                        column: x => x.cancelled_by_user_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_payments_usuarios_registered_by_user_id",
                        column: x => x.registered_by_user_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_payments_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_payments_cancelled_by_user_id",
                table: "employee_payments",
                column: "cancelled_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_payments_period_start_period_end",
                table: "employee_payments",
                columns: new[] { "period_start", "period_end" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_payments_registered_by_user_id",
                table: "employee_payments",
                column: "registered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_payments_status",
                table: "employee_payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_employee_payments_usuario_id_created_at_utc",
                table: "employee_payments",
                columns: new[] { "usuario_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_payments");
        }
    }
}
