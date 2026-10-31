using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddCreditAccountsBlockCV150 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "credito_bloqueo_motivo",
                table: "clientes",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "credito_fecha_actualizacion",
                table: "clientes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "credito_habilitado",
                table: "clientes",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "credito_interes_porcentaje",
                table: "clientes",
                type: "TEXT",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "credito_limite_centavos",
                table: "clientes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "credito_maximo_cuentas_activas",
                table: "clientes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "credito_permitir_saldo_vigente",
                table: "clientes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "credito_plazo_maximo_dias",
                table: "clientes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "credito_usuario_actualizacion_id",
                table: "clientes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cuentas_por_cobrar",
                columns: table => new
                {
                    cuenta_por_cobrar_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_cuenta = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    venta_id = table.Column<int>(type: "INTEGER", nullable: false),
                    cliente_id = table.Column<int>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_registro_id = table.Column<int>(type: "INTEGER", nullable: false),
                    capital_financiado_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    interes_porcentaje = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    interes_calculado_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    total_original_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    total_pagado_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    saldo_actual_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_vencimiento = table.Column<DateTime>(type: "TEXT", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    fecha_pago = table.Column<DateTime>(type: "TEXT", nullable: true),
                    fecha_anulacion = table.Column<DateTime>(type: "TEXT", nullable: true),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cuentas_por_cobrar", x => x.cuenta_por_cobrar_id);
                    table.CheckConstraint("CK_cuentas_por_cobrar_capital_no_negativo", "capital_financiado_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_interes_no_negativo", "interes_calculado_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_pagado_no_negativo", "total_pagado_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_saldo_no_negativo", "saldo_actual_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_total_no_negativo", "total_original_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_cuentas_por_cobrar_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cuentas_por_cobrar_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "cliente_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cuentas_por_cobrar_usuarios_usuario_registro_id",
                        column: x => x.usuario_registro_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cuentas_por_cobrar_ventas_venta_id",
                        column: x => x.venta_id,
                        principalTable: "ventas",
                        principalColumn: "venta_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cuentas_por_cobrar_detalle",
                columns: table => new
                {
                    cuenta_por_cobrar_detalle_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    cuenta_por_cobrar_id = table.Column<int>(type: "INTEGER", nullable: false),
                    detalle_venta_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_codigo_snapshot = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    producto_nombre_snapshot = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    cantidad_financiada = table.Column<int>(type: "INTEGER", nullable: false),
                    precio_unitario_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_linea_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_general_proporcional_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal_financiado_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    capital_pendiente_centavos = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cuentas_por_cobrar_detalle", x => x.cuenta_por_cobrar_detalle_id);
                    table.CheckConstraint("CK_cuentas_por_cobrar_detalle_cantidad_positiva", "cantidad_financiada > 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_detalle_capital_no_negativo", "capital_pendiente_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_detalle_descuento_general_no_negativo", "descuento_general_proporcional_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_detalle_descuento_linea_no_negativo", "descuento_linea_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_detalle_precio_no_negativo", "precio_unitario_centavos >= 0");
                    table.CheckConstraint("CK_cuentas_por_cobrar_detalle_subtotal_no_negativo", "subtotal_financiado_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_cuentas_por_cobrar_detalle_cuentas_por_cobrar_cuenta_por_cobrar_id",
                        column: x => x.cuenta_por_cobrar_id,
                        principalTable: "cuentas_por_cobrar",
                        principalColumn: "cuenta_por_cobrar_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cuentas_por_cobrar_detalle_detalle_ventas_detalle_venta_id",
                        column: x => x.detalle_venta_id,
                        principalTable: "detalle_ventas",
                        principalColumn: "detalle_venta_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cuentas_por_cobrar_detalle_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_credito_usuario_actualizacion_id",
                table: "clientes",
                column: "credito_usuario_actualizacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_caja_id",
                table: "cuentas_por_cobrar",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_cliente_id",
                table: "cuentas_por_cobrar",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_codigo_cuenta",
                table: "cuentas_por_cobrar",
                column: "codigo_cuenta",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_estado",
                table: "cuentas_por_cobrar",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_fecha_vencimiento",
                table: "cuentas_por_cobrar",
                column: "fecha_vencimiento");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_usuario_registro_id",
                table: "cuentas_por_cobrar",
                column: "usuario_registro_id");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_venta_id",
                table: "cuentas_por_cobrar",
                column: "venta_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_detalle_cuenta_por_cobrar_id",
                table: "cuentas_por_cobrar_detalle",
                column: "cuenta_por_cobrar_id");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_detalle_detalle_venta_id",
                table: "cuentas_por_cobrar_detalle",
                column: "detalle_venta_id");

            migrationBuilder.CreateIndex(
                name: "IX_cuentas_por_cobrar_detalle_producto_id",
                table: "cuentas_por_cobrar_detalle",
                column: "producto_id");

            migrationBuilder.AddForeignKey(
                name: "FK_clientes_usuarios_credito_usuario_actualizacion_id",
                table: "clientes",
                column: "credito_usuario_actualizacion_id",
                principalTable: "usuarios",
                principalColumn: "usuario_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO configuracion_sistema (clave, valor, descripcion, fecha_modificacion)
                VALUES
                    ('credito.limite_predeterminado_centavos', '7500', 'Limite predeterminado de credito para clientes nuevos.', CURRENT_TIMESTAMP),
                    ('credito.maximo_cuentas_activas', '3', 'Cantidad maxima predeterminada de cuentas por cobrar abiertas.', CURRENT_TIMESTAMP),
                    ('credito.plazo_predeterminado_dias', '7', 'Plazo predeterminado de credito en dias.', CURRENT_TIMESTAMP),
                    ('credito.plazo_maximo_empleado_dias', '7', 'Plazo maximo que puede usar un EMPLEADO al registrar credito.', CURRENT_TIMESTAMP),
                    ('credito.plazo_maximo_admin_dias', '90', 'Plazo maximo administrativo configurable para credito.', CURRENT_TIMESTAMP),
                    ('credito.interes_predeterminado_porcentaje', '0', 'Interes simple predeterminado para credito.', CURRENT_TIMESTAMP),
                    ('credito.dias_mora', '7', 'Dias de atraso para considerar una cuenta como morosa.', CURRENT_TIMESTAMP),
                    ('credito.permitir_saldo_vigente', 'true', 'Permite crear nuevo credito con saldo vigente no vencido.', CURRENT_TIMESTAMP);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clientes_usuarios_credito_usuario_actualizacion_id",
                table: "clientes");

            migrationBuilder.DropTable(
                name: "cuentas_por_cobrar_detalle");

            migrationBuilder.DropTable(
                name: "cuentas_por_cobrar");

            migrationBuilder.DropIndex(
                name: "IX_clientes_credito_usuario_actualizacion_id",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_bloqueo_motivo",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_fecha_actualizacion",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_habilitado",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_interes_porcentaje",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_limite_centavos",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_maximo_cuentas_activas",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_permitir_saldo_vigente",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_plazo_maximo_dias",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "credito_usuario_actualizacion_id",
                table: "clientes");

            migrationBuilder.Sql("""
                DELETE FROM configuracion_sistema
                WHERE clave IN (
                    'credito.limite_predeterminado_centavos',
                    'credito.maximo_cuentas_activas',
                    'credito.plazo_predeterminado_dias',
                    'credito.plazo_maximo_empleado_dias',
                    'credito.plazo_maximo_admin_dias',
                    'credito.interes_predeterminado_porcentaje',
                    'credito.dias_mora',
                    'credito.permitir_saldo_vigente'
                );
                """);
        }
    }
}
