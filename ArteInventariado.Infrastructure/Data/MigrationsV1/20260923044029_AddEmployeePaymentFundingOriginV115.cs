using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddEmployeePaymentFundingOriginV115 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "caja_id",
                table: "employee_payments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metodo_pago",
                table: "employee_payments",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "operacion_id",
                table: "employee_payments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_fondos",
                table: "employee_payments",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referencia",
                table: "employee_payments",
                type: "TEXT",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "responsable_origen",
                table: "employee_payments",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_payments_caja_id",
                table: "employee_payments",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_payments_operacion_id",
                table: "employee_payments",
                column: "operacion_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_employee_payments_funding_effect",
                table: "employee_payments",
                sql: "origen_fondos IS NULL OR (origen_fondos = 'CAJA_FISICA' AND caja_id IS NOT NULL AND metodo_pago = 'EFECTIVO') OR (origen_fondos IN ('FONDOS_EXTERNOS_DUENO', 'FONDOS_NEGOCIO_FUERA_CAJA') AND caja_id IS NULL AND length(trim(coalesce(responsable_origen, ''))) > 0 AND metodo_pago IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_employee_payments_cajas_caja_id",
                table: "employee_payments",
                column: "caja_id",
                principalTable: "cajas",
                principalColumn: "caja_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employee_payments_cajas_caja_id",
                table: "employee_payments");

            migrationBuilder.DropIndex(
                name: "IX_employee_payments_caja_id",
                table: "employee_payments");

            migrationBuilder.DropIndex(
                name: "IX_employee_payments_operacion_id",
                table: "employee_payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_employee_payments_funding_effect",
                table: "employee_payments");

            migrationBuilder.DropColumn(
                name: "caja_id",
                table: "employee_payments");

            migrationBuilder.DropColumn(
                name: "metodo_pago",
                table: "employee_payments");

            migrationBuilder.DropColumn(
                name: "operacion_id",
                table: "employee_payments");

            migrationBuilder.DropColumn(
                name: "origen_fondos",
                table: "employee_payments");

            migrationBuilder.DropColumn(
                name: "referencia",
                table: "employee_payments");

            migrationBuilder.DropColumn(
                name: "responsable_origen",
                table: "employee_payments");
        }
    }
}
