using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddAccountPaymentsBlockD1V150 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "abonos_cuenta_por_cobrar",
                columns: table => new
                {
                    abono_cuenta_por_cobrar_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_abono = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    operacion_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    cuenta_por_cobrar_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: true),
                    metodo_pago = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    aplicado_interes_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    aplicado_capital_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    saldo_anterior_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    saldo_nuevo_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    interes_pendiente_anterior_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    interes_pendiente_nuevo_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    capital_pendiente_anterior_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    capital_pendiente_nuevo_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    referencia = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    fecha_abono = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_abonos_cuenta_por_cobrar", x => x.abono_cuenta_por_cobrar_id);
                    table.CheckConstraint("CK_abonos_cuenta_aplicado_cuadra", "aplicado_interes_centavos + aplicado_capital_centavos = monto_centavos");
                    table.CheckConstraint("CK_abonos_cuenta_capital_no_negativo", "aplicado_capital_centavos >= 0");
                    table.CheckConstraint("CK_abonos_cuenta_capital_pendiente_no_negativo", "capital_pendiente_anterior_centavos >= 0 AND capital_pendiente_nuevo_centavos >= 0");
                    table.CheckConstraint("CK_abonos_cuenta_interes_no_negativo", "aplicado_interes_centavos >= 0");
                    table.CheckConstraint("CK_abonos_cuenta_interes_pendiente_no_negativo", "interes_pendiente_anterior_centavos >= 0 AND interes_pendiente_nuevo_centavos >= 0");
                    table.CheckConstraint("CK_abonos_cuenta_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_abonos_cuenta_referencia_requerida", "metodo_pago NOT IN ('TRANSFERENCIA', 'BITCOIN') OR length(trim(coalesce(referencia, ''))) > 0");
                    table.CheckConstraint("CK_abonos_cuenta_saldo_anterior_no_negativo", "saldo_anterior_centavos >= 0");
                    table.CheckConstraint("CK_abonos_cuenta_saldo_cuadra", "saldo_anterior_centavos - monto_centavos = saldo_nuevo_centavos");
                    table.CheckConstraint("CK_abonos_cuenta_saldo_disminuye", "saldo_nuevo_centavos <= saldo_anterior_centavos");
                    table.CheckConstraint("CK_abonos_cuenta_saldo_nuevo_no_negativo", "saldo_nuevo_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_abonos_cuenta_por_cobrar_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_abonos_cuenta_por_cobrar_cuentas_por_cobrar_cuenta_por_cobrar_id",
                        column: x => x.cuenta_por_cobrar_id,
                        principalTable: "cuentas_por_cobrar",
                        principalColumn: "cuenta_por_cobrar_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_abonos_cuenta_por_cobrar_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_cobrar_caja_id",
                table: "abonos_cuenta_por_cobrar",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_cobrar_codigo_abono",
                table: "abonos_cuenta_por_cobrar",
                column: "codigo_abono",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_cobrar_cuenta_por_cobrar_id",
                table: "abonos_cuenta_por_cobrar",
                column: "cuenta_por_cobrar_id");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_cobrar_fecha_abono",
                table: "abonos_cuenta_por_cobrar",
                column: "fecha_abono");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_cobrar_metodo_pago",
                table: "abonos_cuenta_por_cobrar",
                column: "metodo_pago");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_cobrar_operacion_id",
                table: "abonos_cuenta_por_cobrar",
                column: "operacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_cobrar_usuario_id",
                table: "abonos_cuenta_por_cobrar",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abonos_cuenta_por_cobrar");
        }
    }
}
