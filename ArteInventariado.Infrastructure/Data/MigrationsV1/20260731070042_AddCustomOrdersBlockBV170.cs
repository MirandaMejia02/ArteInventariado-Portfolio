using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddCustomOrdersBlockBV170 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedidos_personalizados",
                columns: table => new
                {
                    pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_pedido = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    cotizacion_id = table.Column<int>(type: "INTEGER", nullable: true),
                    cliente_id = table.Column<int>(type: "INTEGER", nullable: true),
                    nombre_destinatario_snapshot = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    telefono_snapshot = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    correo_snapshot = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    direccion_snapshot = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    estado = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    prioridad = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    fecha_pedido = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_prometida = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_inicio = table.Column<DateTime>(type: "TEXT", nullable: true),
                    fecha_finalizacion = table.Column<DateTime>(type: "TEXT", nullable: true),
                    fecha_entrega_real = table.Column<DateTime>(type: "TEXT", nullable: true),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_general_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    total_estimado_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descripcion_general = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    especificaciones_generales = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    observaciones_internas = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    notas_cliente = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    usuario_creador_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_ultima_actualizacion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_cancelacion = table.Column<DateTime>(type: "TEXT", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos_personalizados", x => x.pedido_personalizado_id);
                    table.CheckConstraint("CK_pedidos_codigo_obligatorio", "length(trim(codigo_pedido)) > 0");
                    table.CheckConstraint("CK_pedidos_descuento_valido", "descuento_general_centavos >= 0 AND descuento_general_centavos <= subtotal_centavos");
                    table.CheckConstraint("CK_pedidos_destinatario_obligatorio", "length(trim(nombre_destinatario_snapshot)) > 0");
                    table.CheckConstraint("CK_pedidos_fecha_prometida_valida", "fecha_prometida >= fecha_pedido");
                    table.CheckConstraint("CK_pedidos_subtotal_no_negativo", "subtotal_centavos >= 0");
                    table.CheckConstraint("CK_pedidos_total_cuadra", "total_estimado_centavos = subtotal_centavos - descuento_general_centavos");
                    table.ForeignKey(
                        name: "FK_pedidos_personalizados_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "cliente_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pedidos_personalizados_cotizaciones_cotizacion_id",
                        column: x => x.cotizacion_id,
                        principalTable: "cotizaciones",
                        principalColumn: "cotizacion_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_personalizados_usuarios_usuario_creador_id",
                        column: x => x.usuario_creador_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_personalizados_usuarios_usuario_ultima_actualizacion_id",
                        column: x => x.usuario_ultima_actualizacion_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detalle_pedidos_personalizados",
                columns: table => new
                {
                    detalle_pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: true),
                    codigo_producto_snapshot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    nombre_descripcion_snapshot = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    precio_unitario_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_linea_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    especificaciones = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    color = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    medidas = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    material = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    texto_personalizado = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalle_pedidos_personalizados", x => x.detalle_pedido_personalizado_id);
                    table.CheckConstraint("CK_detalle_pedidos_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("CK_detalle_pedidos_descripcion_obligatoria", "length(trim(nombre_descripcion_snapshot)) > 0");
                    table.CheckConstraint("CK_detalle_pedidos_descuento_valido", "descuento_linea_centavos >= 0 AND descuento_linea_centavos <= cantidad * precio_unitario_centavos");
                    table.CheckConstraint("CK_detalle_pedidos_precio_no_negativo", "precio_unitario_centavos >= 0");
                    table.CheckConstraint("CK_detalle_pedidos_subtotal_cuadra", "subtotal_centavos = cantidad * precio_unitario_centavos - descuento_linea_centavos");
                    table.ForeignKey(
                        name: "FK_detalle_pedidos_personalizados_pedidos_personalizados_pedido_personalizado_id",
                        column: x => x.pedido_personalizado_id,
                        principalTable: "pedidos_personalizados",
                        principalColumn: "pedido_personalizado_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_detalle_pedidos_personalizados_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "historial_estado_pedidos",
                columns: table => new
                {
                    historial_estado_pedido_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    pedido_personalizado_id = table.Column<int>(type: "INTEGER", nullable: false),
                    estado_anterior = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    estado_nuevo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historial_estado_pedidos", x => x.historial_estado_pedido_id);
                    table.ForeignKey(
                        name: "FK_historial_estado_pedidos_pedidos_personalizados_pedido_personalizado_id",
                        column: x => x.pedido_personalizado_id,
                        principalTable: "pedidos_personalizados",
                        principalColumn: "pedido_personalizado_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_historial_estado_pedidos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_detalle_pedidos_personalizados_pedido_personalizado_id",
                table: "detalle_pedidos_personalizados",
                column: "pedido_personalizado_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_pedidos_personalizados_producto_id",
                table: "detalle_pedidos_personalizados",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_estado_pedidos_fecha",
                table: "historial_estado_pedidos",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "IX_historial_estado_pedidos_pedido_personalizado_id",
                table: "historial_estado_pedidos",
                column: "pedido_personalizado_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_estado_pedidos_usuario_id",
                table: "historial_estado_pedidos",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_cliente_id",
                table: "pedidos_personalizados",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_codigo_pedido",
                table: "pedidos_personalizados",
                column: "codigo_pedido",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_cotizacion_id",
                table: "pedidos_personalizados",
                column: "cotizacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_estado",
                table: "pedidos_personalizados",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_fecha_pedido",
                table: "pedidos_personalizados",
                column: "fecha_pedido");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_fecha_prometida",
                table: "pedidos_personalizados",
                column: "fecha_prometida");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_prioridad",
                table: "pedidos_personalizados",
                column: "prioridad");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_usuario_creador_id",
                table: "pedidos_personalizados",
                column: "usuario_creador_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_personalizados_usuario_ultima_actualizacion_id",
                table: "pedidos_personalizados",
                column: "usuario_ultima_actualizacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "detalle_pedidos_personalizados");

            migrationBuilder.DropTable(
                name: "historial_estado_pedidos");

            migrationBuilder.DropTable(
                name: "pedidos_personalizados");
        }
    }
}
