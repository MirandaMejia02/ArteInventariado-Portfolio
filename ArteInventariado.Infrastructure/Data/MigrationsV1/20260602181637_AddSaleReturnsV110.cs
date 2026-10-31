using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddSaleReturnsV110 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devoluciones",
                columns: table => new
                {
                    devolucion_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    numero_devolucion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    venta_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: true),
                    fecha_devolucion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    total_devuelto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devoluciones", x => x.devolucion_id);
                    table.CheckConstraint("CK_devoluciones_motivo_obligatorio", "length(trim(motivo)) > 0");
                    table.CheckConstraint("CK_devoluciones_total_no_negativo", "total_devuelto_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_devoluciones_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_devoluciones_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_devoluciones_ventas_venta_id",
                        column: x => x.venta_id,
                        principalTable: "ventas",
                        principalColumn: "venta_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detalle_devoluciones",
                columns: table => new
                {
                    detalle_devolucion_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    devolucion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    detalle_venta_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    precio_unitario_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_proporcional_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal_devuelto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    condicion_producto = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    reintegra_stock = table.Column<bool>(type: "INTEGER", nullable: false),
                    movimiento_inventario_id = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalle_devoluciones", x => x.detalle_devolucion_id);
                    table.CheckConstraint("CK_detalle_devoluciones_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("CK_detalle_devoluciones_descuento_no_negativo", "descuento_proporcional_centavos >= 0");
                    table.CheckConstraint("CK_detalle_devoluciones_precio_no_negativo", "precio_unitario_centavos >= 0");
                    table.CheckConstraint("CK_detalle_devoluciones_subtotal_no_negativo", "subtotal_devuelto_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_detalle_devoluciones_detalle_ventas_detalle_venta_id",
                        column: x => x.detalle_venta_id,
                        principalTable: "detalle_ventas",
                        principalColumn: "detalle_venta_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalle_devoluciones_devoluciones_devolucion_id",
                        column: x => x.devolucion_id,
                        principalTable: "devoluciones",
                        principalColumn: "devolucion_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_detalle_devoluciones_movimientos_inventario_movimiento_inventario_id",
                        column: x => x.movimiento_inventario_id,
                        principalTable: "movimientos_inventario",
                        principalColumn: "movimiento_inventario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalle_devoluciones_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reembolsos",
                columns: table => new
                {
                    reembolso_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    devolucion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: true),
                    metodo_reembolso = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    referencia = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    fecha_reembolso = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reembolsos", x => x.reembolso_id);
                    table.CheckConstraint("CK_reembolsos_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_reembolsos_motivo_obligatorio", "length(trim(motivo)) > 0");
                    table.ForeignKey(
                        name: "FK_reembolsos_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reembolsos_devoluciones_devolucion_id",
                        column: x => x.devolucion_id,
                        principalTable: "devoluciones",
                        principalColumn: "devolucion_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reembolsos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_detalle_devoluciones_detalle_venta_id",
                table: "detalle_devoluciones",
                column: "detalle_venta_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_devoluciones_devolucion_id",
                table: "detalle_devoluciones",
                column: "devolucion_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_devoluciones_movimiento_inventario_id",
                table: "detalle_devoluciones",
                column: "movimiento_inventario_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_devoluciones_producto_id",
                table: "detalle_devoluciones",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_caja_id",
                table: "devoluciones",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_fecha_devolucion",
                table: "devoluciones",
                column: "fecha_devolucion");

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_numero_devolucion",
                table: "devoluciones",
                column: "numero_devolucion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_usuario_id",
                table: "devoluciones",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_venta_id",
                table: "devoluciones",
                column: "venta_id");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_caja_id",
                table: "reembolsos",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_devolucion_id",
                table: "reembolsos",
                column: "devolucion_id");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_fecha_reembolso",
                table: "reembolsos",
                column: "fecha_reembolso");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_usuario_id",
                table: "reembolsos",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "detalle_devoluciones");

            migrationBuilder.DropTable(
                name: "reembolsos");

            migrationBuilder.DropTable(
                name: "devoluciones");
        }
    }
}
