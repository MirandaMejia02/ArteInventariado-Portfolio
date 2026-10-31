using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddSupplierPaymentFundingOriginV115 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "operacion_lote_id",
                table: "pagos_compra",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_fondos",
                table: "pagos_compra",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "responsable_origen",
                table: "pagos_compra",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "operacion_lote_id",
                table: "abonos_cuenta_por_pagar",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_fondos",
                table: "abonos_cuenta_por_pagar",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "responsable_origen",
                table: "abonos_cuenta_por_pagar",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_compra_operacion_lote_id",
                table: "pagos_compra",
                column: "operacion_lote_id");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_pagar_operacion_lote_id",
                table: "abonos_cuenta_por_pagar",
                column: "operacion_lote_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pagos_compra_operacion_lote_id",
                table: "pagos_compra");

            migrationBuilder.DropIndex(
                name: "IX_abonos_cuenta_por_pagar_operacion_lote_id",
                table: "abonos_cuenta_por_pagar");

            migrationBuilder.DropColumn(
                name: "operacion_lote_id",
                table: "pagos_compra");

            migrationBuilder.DropColumn(
                name: "origen_fondos",
                table: "pagos_compra");

            migrationBuilder.DropColumn(
                name: "responsable_origen",
                table: "pagos_compra");

            migrationBuilder.DropColumn(
                name: "operacion_lote_id",
                table: "abonos_cuenta_por_pagar");

            migrationBuilder.DropColumn(
                name: "origen_fondos",
                table: "abonos_cuenta_por_pagar");

            migrationBuilder.DropColumn(
                name: "responsable_origen",
                table: "abonos_cuenta_por_pagar");
        }
    }
}
