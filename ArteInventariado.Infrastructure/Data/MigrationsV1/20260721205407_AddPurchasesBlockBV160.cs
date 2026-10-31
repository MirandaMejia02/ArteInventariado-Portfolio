using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddPurchasesBlockBV160 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compras",
                columns: table => new
                {
                    compra_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_compra = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    proveedor_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_registro_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha_compra = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_registro = table.Column<DateTime>(type: "TEXT", nullable: false),
                    estado_recepcion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    total_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compras", x => x.compra_id);
                    table.CheckConstraint("CK_compras_codigo_obligatorio", "length(trim(codigo_compra)) > 0");
                    table.CheckConstraint("CK_compras_descuento_no_negativo", "descuento_centavos >= 0");
                    table.CheckConstraint("CK_compras_descuento_no_supera_subtotal", "descuento_centavos <= subtotal_centavos");
                    table.CheckConstraint("CK_compras_subtotal_no_negativo", "subtotal_centavos >= 0");
                    table.CheckConstraint("CK_compras_total_no_negativo", "total_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_compras_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalTable: "proveedores",
                        principalColumn: "proveedor_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_compras_usuarios_usuario_registro_id",
                        column: x => x.usuario_registro_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detalle_compras",
                columns: table => new
                {
                    detalle_compra_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    codigo_producto_snapshot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    nombre_producto_snapshot = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    cantidad_comprada = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad_recibida = table.Column<int>(type: "INTEGER", nullable: false),
                    costo_unitario_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_linea_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalle_compras", x => x.detalle_compra_id);
                    table.CheckConstraint("CK_detalle_compras_cantidad_comprada_positiva", "cantidad_comprada > 0");
                    table.CheckConstraint("CK_detalle_compras_cantidad_recibida_no_negativa", "cantidad_recibida >= 0");
                    table.CheckConstraint("CK_detalle_compras_cantidad_recibida_no_supera_comprada", "cantidad_recibida <= cantidad_comprada");
                    table.CheckConstraint("CK_detalle_compras_costo_unitario_no_negativo", "costo_unitario_centavos >= 0");
                    table.CheckConstraint("CK_detalle_compras_descuento_linea_no_negativo", "descuento_linea_centavos >= 0");
                    table.CheckConstraint("CK_detalle_compras_subtotal_no_negativo", "subtotal_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_detalle_compras_compras_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras",
                        principalColumn: "compra_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_detalle_compras_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recepciones_compra",
                columns: table => new
                {
                    recepcion_compra_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_recepcion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    operacion_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_recepcion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha_recepcion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recepciones_compra", x => x.recepcion_compra_id);
                    table.CheckConstraint("CK_recepciones_compra_codigo_obligatorio", "length(trim(codigo_recepcion)) > 0");
                    table.ForeignKey(
                        name: "FK_recepciones_compra_compras_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras",
                        principalColumn: "compra_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recepciones_compra_usuarios_usuario_recepcion_id",
                        column: x => x.usuario_recepcion_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "historial_costos_producto",
                columns: table => new
                {
                    historial_costo_producto_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    proveedor_id = table.Column<int>(type: "INTEGER", nullable: false),
                    compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    detalle_compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    recepcion_compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    costo_anterior_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    costo_recibido_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    costo_promedio_nuevo_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    stock_anterior = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad_recibida = table.Column<int>(type: "INTEGER", nullable: false),
                    stock_nuevo = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historial_costos_producto", x => x.historial_costo_producto_id);
                    table.CheckConstraint("CK_historial_costos_cantidad_recibida_positiva", "cantidad_recibida > 0");
                    table.CheckConstraint("CK_historial_costos_costo_anterior_no_negativo", "costo_anterior_centavos >= 0");
                    table.CheckConstraint("CK_historial_costos_costo_promedio_no_negativo", "costo_promedio_nuevo_centavos >= 0");
                    table.CheckConstraint("CK_historial_costos_costo_recibido_no_negativo", "costo_recibido_centavos >= 0");
                    table.CheckConstraint("CK_historial_costos_stock_anterior_no_negativo", "stock_anterior >= 0");
                    table.CheckConstraint("CK_historial_costos_stock_nuevo_no_negativo", "stock_nuevo >= 0");
                    table.ForeignKey(
                        name: "FK_historial_costos_producto_compras_compra_id",
                        column: x => x.compra_id,
                        principalTable: "compras",
                        principalColumn: "compra_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_historial_costos_producto_detalle_compras_detalle_compra_id",
                        column: x => x.detalle_compra_id,
                        principalTable: "detalle_compras",
                        principalColumn: "detalle_compra_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_historial_costos_producto_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_historial_costos_producto_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalTable: "proveedores",
                        principalColumn: "proveedor_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_historial_costos_producto_recepciones_compra_recepcion_compra_id",
                        column: x => x.recepcion_compra_id,
                        principalTable: "recepciones_compra",
                        principalColumn: "recepcion_compra_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_historial_costos_producto_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recepcion_compra_detalles",
                columns: table => new
                {
                    recepcion_compra_detalle_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    recepcion_compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    detalle_compra_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad_recibida = table.Column<int>(type: "INTEGER", nullable: false),
                    stock_anterior = table.Column<int>(type: "INTEGER", nullable: false),
                    stock_nuevo = table.Column<int>(type: "INTEGER", nullable: false),
                    costo_unitario_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    costo_promedio_anterior_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    costo_promedio_nuevo_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    movimiento_inventario_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recepcion_compra_detalles", x => x.recepcion_compra_detalle_id);
                    table.CheckConstraint("CK_recepcion_compra_detalles_cantidad_positiva", "cantidad_recibida > 0");
                    table.CheckConstraint("CK_recepcion_compra_detalles_costo_promedio_anterior_no_negativo", "costo_promedio_anterior_centavos >= 0");
                    table.CheckConstraint("CK_recepcion_compra_detalles_costo_promedio_nuevo_no_negativo", "costo_promedio_nuevo_centavos >= 0");
                    table.CheckConstraint("CK_recepcion_compra_detalles_costo_unitario_no_negativo", "costo_unitario_centavos >= 0");
                    table.CheckConstraint("CK_recepcion_compra_detalles_stock_anterior_no_negativo", "stock_anterior >= 0");
                    table.CheckConstraint("CK_recepcion_compra_detalles_stock_nuevo_no_negativo", "stock_nuevo >= 0");
                    table.ForeignKey(
                        name: "FK_recepcion_compra_detalles_detalle_compras_detalle_compra_id",
                        column: x => x.detalle_compra_id,
                        principalTable: "detalle_compras",
                        principalColumn: "detalle_compra_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recepcion_compra_detalles_movimientos_inventario_movimiento_inventario_id",
                        column: x => x.movimiento_inventario_id,
                        principalTable: "movimientos_inventario",
                        principalColumn: "movimiento_inventario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recepcion_compra_detalles_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recepcion_compra_detalles_recepciones_compra_recepcion_compra_id",
                        column: x => x.recepcion_compra_id,
                        principalTable: "recepciones_compra",
                        principalColumn: "recepcion_compra_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_compras_codigo_compra",
                table: "compras",
                column: "codigo_compra",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_compras_estado_recepcion",
                table: "compras",
                column: "estado_recepcion");

            migrationBuilder.CreateIndex(
                name: "IX_compras_fecha_compra",
                table: "compras",
                column: "fecha_compra");

            migrationBuilder.CreateIndex(
                name: "IX_compras_proveedor_id",
                table: "compras",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "IX_compras_usuario_registro_id",
                table: "compras",
                column: "usuario_registro_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_compras_compra_id",
                table: "detalle_compras",
                column: "compra_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_compras_compra_id_producto_id",
                table: "detalle_compras",
                columns: new[] { "compra_id", "producto_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_detalle_compras_producto_id",
                table: "detalle_compras",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_costos_producto_compra_id",
                table: "historial_costos_producto",
                column: "compra_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_costos_producto_detalle_compra_id",
                table: "historial_costos_producto",
                column: "detalle_compra_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_costos_producto_fecha",
                table: "historial_costos_producto",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "IX_historial_costos_producto_producto_id",
                table: "historial_costos_producto",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_costos_producto_proveedor_id",
                table: "historial_costos_producto",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_costos_producto_recepcion_compra_id",
                table: "historial_costos_producto",
                column: "recepcion_compra_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_costos_producto_usuario_id",
                table: "historial_costos_producto",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_recepcion_compra_detalles_detalle_compra_id",
                table: "recepcion_compra_detalles",
                column: "detalle_compra_id");

            migrationBuilder.CreateIndex(
                name: "IX_recepcion_compra_detalles_movimiento_inventario_id",
                table: "recepcion_compra_detalles",
                column: "movimiento_inventario_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recepcion_compra_detalles_producto_id",
                table: "recepcion_compra_detalles",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_recepcion_compra_detalles_recepcion_compra_id",
                table: "recepcion_compra_detalles",
                column: "recepcion_compra_id");

            migrationBuilder.CreateIndex(
                name: "IX_recepciones_compra_codigo_recepcion",
                table: "recepciones_compra",
                column: "codigo_recepcion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recepciones_compra_compra_id",
                table: "recepciones_compra",
                column: "compra_id");

            migrationBuilder.CreateIndex(
                name: "IX_recepciones_compra_fecha_recepcion",
                table: "recepciones_compra",
                column: "fecha_recepcion");

            migrationBuilder.CreateIndex(
                name: "IX_recepciones_compra_operacion_id",
                table: "recepciones_compra",
                column: "operacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recepciones_compra_usuario_recepcion_id",
                table: "recepciones_compra",
                column: "usuario_recepcion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historial_costos_producto");

            migrationBuilder.DropTable(
                name: "recepcion_compra_detalles");

            migrationBuilder.DropTable(
                name: "detalle_compras");

            migrationBuilder.DropTable(
                name: "recepciones_compra");

            migrationBuilder.DropTable(
                name: "compras");
        }
    }
}
