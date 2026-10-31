using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddSaleReceiptPaymentTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cambio_centavos",
                table: "ventas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "pagado_centavos",
                table: "ventas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ventas_cambio_no_negativo",
                table: "ventas",
                sql: "cambio_centavos >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ventas_pagado_no_negativo",
                table: "ventas",
                sql: "pagado_centavos >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ventas_cambio_no_negativo",
                table: "ventas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ventas_pagado_no_negativo",
                table: "ventas");

            migrationBuilder.DropColumn(
                name: "cambio_centavos",
                table: "ventas");

            migrationBuilder.DropColumn(
                name: "pagado_centavos",
                table: "ventas");
        }
    }
}
