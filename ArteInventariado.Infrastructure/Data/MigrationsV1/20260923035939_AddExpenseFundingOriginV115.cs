using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddExpenseFundingOriginV115 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "caja_id",
                table: "gastos",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<Guid>(
                name: "operacion_id",
                table: "gastos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_fondos",
                table: "gastos",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "responsable_origen",
                table: "gastos",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_gastos_operacion_id",
                table: "gastos",
                column: "operacion_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_gastos_operacion_id",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "operacion_id",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "origen_fondos",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "responsable_origen",
                table: "gastos");

            migrationBuilder.AlterColumn<int>(
                name: "caja_id",
                table: "gastos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
