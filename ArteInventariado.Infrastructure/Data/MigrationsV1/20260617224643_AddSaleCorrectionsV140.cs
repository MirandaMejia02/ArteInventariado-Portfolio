using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddSaleCorrectionsV140 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tipo_correccion",
                table: "devoluciones",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "DEVOLUCION");

            migrationBuilder.CreateTable(
                name: "detalle_cambios_venta",
                columns: table => new
                {
                    detalle_cambio_venta_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    devolucion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    detalle_venta_origen_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_nuevo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    precio_unitario_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    total_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    movimiento_inventario_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalle_cambios_venta", x => x.detalle_cambio_venta_id);
                    table.CheckConstraint("CK_detalle_cambios_venta_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("CK_detalle_cambios_venta_descuento_no_negativo", "descuento_centavos >= 0");
                    table.CheckConstraint("CK_detalle_cambios_venta_precio_no_negativo", "precio_unitario_centavos >= 0");
                    table.CheckConstraint("CK_detalle_cambios_venta_subtotal_no_negativo", "subtotal_centavos >= 0");
                    table.CheckConstraint("CK_detalle_cambios_venta_total_no_negativo", "total_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_detalle_cambios_venta_detalle_ventas_detalle_venta_origen_id",
                        column: x => x.detalle_venta_origen_id,
                        principalTable: "detalle_ventas",
                        principalColumn: "detalle_venta_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalle_cambios_venta_devoluciones_devolucion_id",
                        column: x => x.devolucion_id,
                        principalTable: "devoluciones",
                        principalColumn: "devolucion_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_detalle_cambios_venta_movimientos_inventario_movimiento_inventario_id",
                        column: x => x.movimiento_inventario_id,
                        principalTable: "movimientos_inventario",
                        principalColumn: "movimiento_inventario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalle_cambios_venta_productos_producto_nuevo_id",
                        column: x => x.producto_nuevo_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mermas",
                columns: table => new
                {
                    merma_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    devolucion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    detalle_devolucion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    venta_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    condicion_producto = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    fecha_merma = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mermas", x => x.merma_id);
                    table.CheckConstraint("CK_mermas_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("CK_mermas_motivo_obligatorio", "length(trim(motivo)) > 0");
                    table.ForeignKey(
                        name: "FK_mermas_detalle_devoluciones_detalle_devolucion_id",
                        column: x => x.detalle_devolucion_id,
                        principalTable: "detalle_devoluciones",
                        principalColumn: "detalle_devolucion_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mermas_devoluciones_devolucion_id",
                        column: x => x.devolucion_id,
                        principalTable: "devoluciones",
                        principalColumn: "devolucion_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mermas_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mermas_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mermas_ventas_venta_id",
                        column: x => x.venta_id,
                        principalTable: "ventas",
                        principalColumn: "venta_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pagos_correccion_venta",
                columns: table => new
                {
                    pago_correccion_venta_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    devolucion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    venta_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: true),
                    metodo_pago = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    referencia = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    fecha_pago = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos_correccion_venta", x => x.pago_correccion_venta_id);
                    table.CheckConstraint("CK_pagos_correccion_venta_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_pagos_correccion_venta_motivo_obligatorio", "length(trim(motivo)) > 0");
                    table.ForeignKey(
                        name: "FK_pagos_correccion_venta_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_correccion_venta_devoluciones_devolucion_id",
                        column: x => x.devolucion_id,
                        principalTable: "devoluciones",
                        principalColumn: "devolucion_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pagos_correccion_venta_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_correccion_venta_ventas_venta_id",
                        column: x => x.venta_id,
                        principalTable: "ventas",
                        principalColumn: "venta_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_venta_id_tipo_correccion",
                table: "devoluciones",
                columns: new[] { "venta_id", "tipo_correccion" },
                unique: true,
                filter: "tipo_correccion = 'ANULACION'");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_cambios_venta_detalle_venta_origen_id",
                table: "detalle_cambios_venta",
                column: "detalle_venta_origen_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_cambios_venta_devolucion_id",
                table: "detalle_cambios_venta",
                column: "devolucion_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_cambios_venta_movimiento_inventario_id",
                table: "detalle_cambios_venta",
                column: "movimiento_inventario_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_cambios_venta_producto_nuevo_id",
                table: "detalle_cambios_venta",
                column: "producto_nuevo_id");

            migrationBuilder.CreateIndex(
                name: "IX_mermas_detalle_devolucion_id",
                table: "mermas",
                column: "detalle_devolucion_id");

            migrationBuilder.CreateIndex(
                name: "IX_mermas_devolucion_id",
                table: "mermas",
                column: "devolucion_id");

            migrationBuilder.CreateIndex(
                name: "IX_mermas_fecha_merma",
                table: "mermas",
                column: "fecha_merma");

            migrationBuilder.CreateIndex(
                name: "IX_mermas_producto_id",
                table: "mermas",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_mermas_usuario_id",
                table: "mermas",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_mermas_venta_id",
                table: "mermas",
                column: "venta_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_correccion_venta_caja_id",
                table: "pagos_correccion_venta",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_correccion_venta_devolucion_id",
                table: "pagos_correccion_venta",
                column: "devolucion_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_correccion_venta_fecha_pago",
                table: "pagos_correccion_venta",
                column: "fecha_pago");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_correccion_venta_usuario_id",
                table: "pagos_correccion_venta",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_correccion_venta_venta_id",
                table: "pagos_correccion_venta",
                column: "venta_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "detalle_cambios_venta");

            migrationBuilder.DropTable(
                name: "mermas");

            migrationBuilder.DropTable(
                name: "pagos_correccion_venta");

            migrationBuilder.DropIndex(
                name: "IX_devoluciones_venta_id_tipo_correccion",
                table: "devoluciones");

            migrationBuilder.DropColumn(
                name: "tipo_correccion",
                table: "devoluciones");
        }
    }
}
