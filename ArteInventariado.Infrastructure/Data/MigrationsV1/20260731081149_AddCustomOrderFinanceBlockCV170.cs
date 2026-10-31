using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddCustomOrderFinanceBlockCV170 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "estado_financiero",
                table: "pedidos_personalizados",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "SIN_PAGO");

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_conversion",
                table: "pedidos_personalizados",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "operacion_conversion_id",
                table: "pedidos_personalizados",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "saldo_pendiente_centavos",
                table: "pedidos_personalizados",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "total_pagado_centavos",
                table: "pedidos_personalizados",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "total_reembolsado_centavos",
                table: "pedidos_personalizados",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "usuario_conversion_id",
                table: "pedidos_personalizados",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "venta_id",
                table: "pedidos_personalizados",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "afecta_caja",
                table: "pagos_venta",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "origen",
                table: "pagos_venta",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "VENTA");

            migrationBuilder.AddColumn<int>(
                name: "pago_pedido_personalizado_id",
                table: "pagos_venta",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE pedidos_personalizados SET saldo_pendiente_centavos = total_estimado_centavos, estado_financiero = 'SIN_PAGO';");

            migrationBuilder.Sql(
                "UPDATE pagos_venta SET origen = 'VENTA', afecta_caja = 1;");

            migrationBuilder.CreateTable(
                name: "pagos_pedido_personalizado",
                columns: table => new
                {
                    pago_pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_pago = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    operacion_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false),
                    tipo_pago = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    metodo_pago = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    referencia = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    afecta_caja = table.Column<bool>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: true),
                    saldo_anterior_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    saldo_nuevo_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    venta_id_aplicada = table.Column<int>(type: "INTEGER", nullable: true),
                    fecha_aplicacion_venta = table.Column<DateTime>(type: "TEXT", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos_pedido_personalizado", x => x.pago_pedido_personalizado_id);
                    table.CheckConstraint("CK_pago_pedido_codigo_obligatorio", "length(trim(codigo_pago)) > 0");
                    table.CheckConstraint("CK_pago_pedido_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_pago_pedido_saldos_no_negativos", "saldo_anterior_centavos >= 0 AND saldo_nuevo_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_pagos_pedido_personalizado_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_pedido_personalizado_pedidos_personalizados_pedido_personalizado_id",
                        column: x => x.pedido_personalizado_id,
                        principalTable: "pedidos_personalizados",
                        principalColumn: "pedido_personalizado_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_pedido_personalizado_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_pedido_personalizado_ventas_venta_id_aplicada",
                        column: x => x.venta_id_aplicada,
                        principalTable: "ventas",
                        principalColumn: "venta_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservas_inventario_pedido",
                columns: table => new
                {
                    reserva_inventario_pedido_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false),
                    detalle_pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad_reservada = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad_consumida = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad_liberada = table.Column<int>(type: "INTEGER", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    fecha_reserva = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_consumo = table.Column<DateTime>(type: "TEXT", nullable: true),
                    fecha_liberacion = table.Column<DateTime>(type: "TEXT", nullable: true),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservas_inventario_pedido", x => x.reserva_inventario_pedido_id);
                    table.CheckConstraint("CK_reserva_cantidad_positiva", "cantidad_reservada > 0");
                    table.CheckConstraint("CK_reserva_cantidades_validas", "cantidad_consumida >= 0 AND cantidad_liberada >= 0 AND cantidad_consumida + cantidad_liberada <= cantidad_reservada");
                    table.ForeignKey(
                        name: "FK_reservas_inventario_pedido_detalle_pedidos_personalizados_detalle_pedido_personalizado_id",
                        column: x => x.detalle_pedido_personalizado_id,
                        principalTable: "detalle_pedidos_personalizados",
                        principalColumn: "detalle_pedido_personalizado_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservas_inventario_pedido_pedidos_personalizados_pedido_personalizado_id",
                        column: x => x.pedido_personalizado_id,
                        principalTable: "pedidos_personalizados",
                        principalColumn: "pedido_personalizado_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservas_inventario_pedido_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservas_inventario_pedido_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reembolsos_pedido_personalizado",
                columns: table => new
                {
                    reembolso_pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_reembolso = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    operacion_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false),
                    pago_pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: true),
                    metodo_pago = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    referencia = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    afecta_caja = table.Column<bool>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: true),
                    saldo_anterior_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    saldo_nuevo_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reembolsos_pedido_personalizado", x => x.reembolso_pedido_personalizado_id);
                    table.CheckConstraint("CK_reembolso_pedido_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_reembolso_pedido_motivo_obligatorio", "length(trim(motivo)) > 0");
                    table.CheckConstraint("CK_reembolso_pedido_saldos_no_negativos", "saldo_anterior_centavos >= 0 AND saldo_nuevo_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_reembolsos_pedido_personalizado_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reembolsos_pedido_personalizado_pagos_pedido_personalizado_pago_pedido_personalizado_id",
                        column: x => x.pago_pedido_personalizado_id,
                        principalTable: "pagos_pedido_personalizado",
                        principalColumn: "pago_pedido_personalizado_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reembolsos_pedido_personalizado_pedidos_personalizados_pedido_personalizado_id",
                        column: x => x.pedido_personalizado_id,
                        principalTable: "pedidos_personalizados",
                        principalColumn: "pedido_personalizado_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reembolsos_pedido_personalizado_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_estado_financiero",
                table: "pedidos_personalizados",
                column: "estado_financiero");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_operacion_conversion_id",
                table: "pedidos_personalizados",
                column: "operacion_conversion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_usuario_conversion_id",
                table: "pedidos_personalizados",
                column: "usuario_conversion_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_venta_id",
                table: "pedidos_personalizados",
                column: "venta_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_pedidos_totales_financieros",
                table: "pedidos_personalizados",
                sql: "total_pagado_centavos >= 0 AND total_reembolsado_centavos >= 0 AND saldo_pendiente_centavos >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_venta_pago_pedido_personalizado_id",
                table: "pagos_venta",
                column: "pago_pedido_personalizado_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_pedido_personalizado_caja_id",
                table: "pagos_pedido_personalizado",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_pedido_personalizado_codigo_pago",
                table: "pagos_pedido_personalizado",
                column: "codigo_pago",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_pedido_personalizado_fecha",
                table: "pagos_pedido_personalizado",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_pedido_personalizado_operacion_id",
                table: "pagos_pedido_personalizado",
                column: "operacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_pedido_personalizado_pedido_personalizado_id",
                table: "pagos_pedido_personalizado",
                column: "pedido_personalizado_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_pedido_personalizado_usuario_id",
                table: "pagos_pedido_personalizado",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_pedido_personalizado_venta_id_aplicada",
                table: "pagos_pedido_personalizado",
                column: "venta_id_aplicada");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_pedido_personalizado_caja_id",
                table: "reembolsos_pedido_personalizado",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_pedido_personalizado_codigo_reembolso",
                table: "reembolsos_pedido_personalizado",
                column: "codigo_reembolso",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_pedido_personalizado_fecha",
                table: "reembolsos_pedido_personalizado",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_pedido_personalizado_operacion_id",
                table: "reembolsos_pedido_personalizado",
                column: "operacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_pedido_personalizado_pago_pedido_personalizado_id",
                table: "reembolsos_pedido_personalizado",
                column: "pago_pedido_personalizado_id");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_pedido_personalizado_pedido_personalizado_id",
                table: "reembolsos_pedido_personalizado",
                column: "pedido_personalizado_id");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_pedido_personalizado_usuario_id",
                table: "reembolsos_pedido_personalizado",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_inventario_pedido_detalle_pedido_personalizado_id_producto_id",
                table: "reservas_inventario_pedido",
                columns: new[] { "detalle_pedido_personalizado_id", "producto_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservas_inventario_pedido_estado",
                table: "reservas_inventario_pedido",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_inventario_pedido_pedido_personalizado_id",
                table: "reservas_inventario_pedido",
                column: "pedido_personalizado_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_inventario_pedido_producto_id",
                table: "reservas_inventario_pedido",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_inventario_pedido_usuario_id",
                table: "reservas_inventario_pedido",
                column: "usuario_id");

            migrationBuilder.AddForeignKey(
                name: "FK_pagos_venta_pagos_pedido_personalizado_pago_pedido_personalizado_id",
                table: "pagos_venta",
                column: "pago_pedido_personalizado_id",
                principalTable: "pagos_pedido_personalizado",
                principalColumn: "pago_pedido_personalizado_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pedidos_personalizados_usuarios_usuario_conversion_id",
                table: "pedidos_personalizados",
                column: "usuario_conversion_id",
                principalTable: "usuarios",
                principalColumn: "usuario_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pedidos_personalizados_ventas_venta_id",
                table: "pedidos_personalizados",
                column: "venta_id",
                principalTable: "ventas",
                principalColumn: "venta_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pagos_venta_pagos_pedido_personalizado_pago_pedido_personalizado_id",
                table: "pagos_venta");

            migrationBuilder.DropForeignKey(
                name: "FK_pedidos_personalizados_usuarios_usuario_conversion_id",
                table: "pedidos_personalizados");

            migrationBuilder.DropForeignKey(
                name: "FK_pedidos_personalizados_ventas_venta_id",
                table: "pedidos_personalizados");

            migrationBuilder.DropTable(
                name: "reembolsos_pedido_personalizado");

            migrationBuilder.DropTable(
                name: "reservas_inventario_pedido");

            migrationBuilder.DropTable(
                name: "pagos_pedido_personalizado");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_personalizados_estado_financiero",
                table: "pedidos_personalizados");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_personalizados_operacion_conversion_id",
                table: "pedidos_personalizados");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_personalizados_usuario_conversion_id",
                table: "pedidos_personalizados");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_personalizados_venta_id",
                table: "pedidos_personalizados");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pedidos_totales_financieros",
                table: "pedidos_personalizados");

            migrationBuilder.DropIndex(
                name: "IX_pagos_venta_pago_pedido_personalizado_id",
                table: "pagos_venta");

            migrationBuilder.DropColumn(
                name: "estado_financiero",
                table: "pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "fecha_conversion",
                table: "pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "operacion_conversion_id",
                table: "pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "saldo_pendiente_centavos",
                table: "pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "total_pagado_centavos",
                table: "pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "total_reembolsado_centavos",
                table: "pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "usuario_conversion_id",
                table: "pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "venta_id",
                table: "pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "afecta_caja",
                table: "pagos_venta");

            migrationBuilder.DropColumn(
                name: "origen",
                table: "pagos_venta");

            migrationBuilder.DropColumn(
                name: "pago_pedido_personalizado_id",
                table: "pagos_venta");
        }
    }
}
