using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddSupplierPaymentsBlockCV160 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "estado_financiero",
                table: "compras",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "PENDIENTE");

            migrationBuilder.AddColumn<long>(
                name: "saldo_pendiente_centavos",
                table: "compras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "total_pagado_centavos",
                table: "compras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Backfill seguro: las compras existentes no tienen pagos registrados.
            // Se conserva su total completo como saldo, sin inventar pagos ni afectar caja.
            migrationBuilder.Sql(
                "UPDATE compras SET estado_financiero = 'PENDIENTE', total_pagado_centavos = 0, saldo_pendiente_centavos = total_centavos;");

            migrationBuilder.CreateTable(
                name: "cuentas_por_pagar",
                columns: table => new
                {
                    cuenta_por_pagar_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_cuenta = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    proveedor_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_creador_id = table.Column<int>(type: "INTEGER", nullable: false),
                    total_original_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    total_pagado_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    saldo_pendiente_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_vencimiento = table.Column<DateTime>(type: "TEXT", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    fecha_pago = table.Column<DateTime>(type: "TEXT", nullable: true),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cuentas_por_pagar", x => x.cuenta_por_pagar_id);
                    table.CheckConstraint("CK_cuentas_pagar_pagado_no_negativo", "total_pagado_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_pagar_saldo_no_negativo", "saldo_pendiente_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_pagar_total_no_negativo", "total_original_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_pagar_totales_cuadran", "total_pagado_centavos + saldo_pendiente_centavos = total_original_centavos");
                    table.ForeignKey(
                        name: "FK_cuentas_por_pagar_compras_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras",
                        principalColumn: "compra_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cuentas_por_pagar_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalTable: "proveedores",
                        principalColumn: "proveedor_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cuentas_por_pagar_usuarios_usuario_creador_id",
                        column: x => x.usuario_creador_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pagos_compra",
                columns: table => new
                {
                    pago_compra_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_pago = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    operacion_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: true),
                    metodo_pago = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    referencia = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    fecha_pago = table.Column<DateTime>(type: "TEXT", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    es_pago_inicial = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos_compra", x => x.pago_compra_id);
                    table.CheckConstraint("CK_pagos_compra_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_pagos_compra_referencia_requerida", "metodo_pago NOT IN ('TRANSFERENCIA', 'BITCOIN') OR length(trim(coalesce(referencia, ''))) > 0");
                    table.ForeignKey(
                        name: "FK_pagos_compra_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_compra_compras_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras",
                        principalColumn: "compra_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_compra_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "abonos_cuenta_por_pagar",
                columns: table => new
                {
                    abono_cuenta_por_pagar_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_abono = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    operacion_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    cuenta_por_pagar_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: true),
                    metodo_pago = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    referencia = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    fecha_abono = table.Column<DateTime>(type: "TEXT", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    saldo_anterior_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    saldo_nuevo_centavos = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_abonos_cuenta_por_pagar", x => x.abono_cuenta_por_pagar_id);
                    table.CheckConstraint("CK_abonos_pagar_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_abonos_pagar_referencia_requerida", "metodo_pago NOT IN ('TRANSFERENCIA', 'BITCOIN') OR length(trim(coalesce(referencia, ''))) > 0");
                    table.CheckConstraint("CK_abonos_pagar_saldo_anterior_no_negativo", "saldo_anterior_centavos >= 0");
                    table.CheckConstraint("CK_abonos_pagar_saldo_cuadra", "saldo_anterior_centavos - monto_centavos = saldo_nuevo_centavos");
                    table.CheckConstraint("CK_abonos_pagar_saldo_nuevo_no_negativo", "saldo_nuevo_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_abonos_cuenta_por_pagar_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_abonos_cuenta_por_pagar_cuentas_por_pagar_cuenta_por_pagar_id",
                        column: x => x.cuenta_por_pagar_id,
                        principalTable: "cuentas_por_pagar",
                        principalColumn: "cuenta_por_pagar_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_abonos_cuenta_por_pagar_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Una cuenta trazable por cada compra previa. Se usa un vencimiento tecnico
            // de 30 dias desde la compra porque los datos historicos no contienen ese valor.
            migrationBuilder.Sql(
                """
                INSERT INTO cuentas_por_pagar (
                    codigo_cuenta,
                    compra_id,
                    proveedor_id,
                    usuario_creador_id,
                    total_original_centavos,
                    total_pagado_centavos,
                    saldo_pendiente_centavos,
                    fecha_creacion,
                    fecha_vencimiento,
                    estado,
                    fecha_pago,
                    fecha_actualizacion,
                    observaciones)
                SELECT
                    'CPP-' || printf('%06d', compra_id),
                    compra_id,
                    proveedor_id,
                    usuario_registro_id,
                    total_centavos,
                    0,
                    total_centavos,
                    fecha_registro,
                    datetime(fecha_compra, '+30 days'),
                    CASE
                        WHEN date(fecha_compra, '+30 days') < date('now') THEN 'VENCIDA'
                        ELSE 'ABIERTA'
                    END,
                    NULL,
                    CURRENT_TIMESTAMP,
                    'Backfill v1.6.0 Bloque C: compra previa sin pagos; vencimiento tecnico a 30 dias.'
                FROM compras;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_compras_estado_financiero",
                table: "compras",
                column: "estado_financiero");

            migrationBuilder.AddCheckConstraint(
                name: "CK_compras_finanzas_cuadran",
                table: "compras",
                sql: "total_pagado_centavos + saldo_pendiente_centavos = total_centavos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_compras_pagado_no_negativo",
                table: "compras",
                sql: "total_pagado_centavos >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_compras_saldo_no_negativo",
                table: "compras",
                sql: "saldo_pendiente_centavos >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_pagar_caja_id",
                table: "abonos_cuenta_por_pagar",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_pagar_codigo_abono",
                table: "abonos_cuenta_por_pagar",
                column: "codigo_abono",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_pagar_cuenta_por_pagar_id",
                table: "abonos_cuenta_por_pagar",
                column: "cuenta_por_pagar_id");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_pagar_fecha_abono",
                table: "abonos_cuenta_por_pagar",
                column: "fecha_abono");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_pagar_metodo_pago",
                table: "abonos_cuenta_por_pagar",
                column: "metodo_pago");

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_pagar_operacion_id",
                table: "abonos_cuenta_por_pagar",
                column: "operacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_por_pagar_usuario_id",
                table: "abonos_cuenta_por_pagar",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_pagar_codigo_cuenta",
                table: "cuentas_por_pagar",
                column: "codigo_cuenta",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_pagar_compra_id",
                table: "cuentas_por_pagar",
                column: "compra_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_pagar_estado",
                table: "cuentas_por_pagar",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_pagar_fecha_vencimiento",
                table: "cuentas_por_pagar",
                column: "fecha_vencimiento");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_pagar_proveedor_id",
                table: "cuentas_por_pagar",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_pagar_usuario_creador_id",
                table: "cuentas_por_pagar",
                column: "usuario_creador_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_compra_caja_id",
                table: "pagos_compra",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_compra_codigo_pago",
                table: "pagos_compra",
                column: "codigo_pago",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_compra_compra_id",
                table: "pagos_compra",
                column: "compra_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_compra_fecha_pago",
                table: "pagos_compra",
                column: "fecha_pago");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_compra_metodo_pago",
                table: "pagos_compra",
                column: "metodo_pago");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_compra_operacion_id",
                table: "pagos_compra",
                column: "operacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_compra_usuario_id",
                table: "pagos_compra",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abonos_cuenta_por_pagar");

            migrationBuilder.DropTable(
                name: "pagos_compra");

            migrationBuilder.DropTable(
                name: "cuentas_por_pagar");

            migrationBuilder.DropIndex(
                name: "IX_compras_estado_financiero",
                table: "compras");

            migrationBuilder.DropCheckConstraint(
                name: "CK_compras_finanzas_cuadran",
                table: "compras");

            migrationBuilder.DropCheckConstraint(
                name: "CK_compras_pagado_no_negativo",
                table: "compras");

            migrationBuilder.DropCheckConstraint(
                name: "CK_compras_saldo_no_negativo",
                table: "compras");

            migrationBuilder.DropColumn(
                name: "estado_financiero",
                table: "compras");

            migrationBuilder.DropColumn(
                name: "saldo_pendiente_centavos",
                table: "compras");

            migrationBuilder.DropColumn(
                name: "total_pagado_centavos",
                table: "compras");
        }
    }
}
