using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddQuotationsBlockAV170 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cotizaciones",
                columns: table => new
                {
                    cotizacion_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_cotizacion = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    cliente_id = table.Column<int>(type: "INTEGER", nullable: true),
                    nombre_destinatario_snapshot = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    telefono_snapshot = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    correo_snapshot = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    direccion_snapshot = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_vencimiento = table.Column<DateTime>(type: "TEXT", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_general_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    total_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    condiciones_comerciales = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    usuario_creador_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_ultima_actualizacion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_emision = table.Column<DateTime>(type: "TEXT", nullable: true),
                    fecha_cierre = table.Column<DateTime>(type: "TEXT", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cotizaciones", x => x.cotizacion_id);
                    table.CheckConstraint("CK_cotizaciones_codigo_obligatorio", "length(trim(codigo_cotizacion)) > 0");
                    table.CheckConstraint("CK_cotizaciones_descuento_no_negativo", "descuento_general_centavos >= 0");
                    table.CheckConstraint("CK_cotizaciones_descuento_valido", "descuento_general_centavos <= subtotal_centavos");
                    table.CheckConstraint("CK_cotizaciones_destinatario_obligatorio", "length(trim(nombre_destinatario_snapshot)) > 0");
                    table.CheckConstraint("CK_cotizaciones_subtotal_no_negativo", "subtotal_centavos >= 0");
                    table.CheckConstraint("CK_cotizaciones_total_cuadra", "total_centavos = subtotal_centavos - descuento_general_centavos");
                    table.CheckConstraint("CK_cotizaciones_total_no_negativo", "total_centavos >= 0");
                    table.CheckConstraint("CK_cotizaciones_vencimiento_valido", "fecha_vencimiento >= fecha");
                    table.ForeignKey(
                        name: "FK_cotizaciones_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "cliente_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_cotizaciones_usuarios_usuario_creador_id",
                        column: x => x.usuario_creador_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cotizaciones_usuarios_usuario_ultima_actualizacion_id",
                        column: x => x.usuario_ultima_actualizacion_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detalle_cotizaciones",
                columns: table => new
                {
                    detalle_cotizacion_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    cotizacion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: true),
                    codigo_producto_snapshot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    nombre_descripcion_snapshot = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    precio_unitario_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_linea_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalle_cotizaciones", x => x.detalle_cotizacion_id);
                    table.CheckConstraint("CK_detalle_cotizaciones_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("CK_detalle_cotizaciones_descripcion_obligatoria", "length(trim(nombre_descripcion_snapshot)) > 0");
                    table.CheckConstraint("CK_detalle_cotizaciones_descuento_no_negativo", "descuento_linea_centavos >= 0");
                    table.CheckConstraint("CK_detalle_cotizaciones_descuento_valido", "descuento_linea_centavos <= cantidad * precio_unitario_centavos");
                    table.CheckConstraint("CK_detalle_cotizaciones_precio_no_negativo", "precio_unitario_centavos >= 0");
                    table.CheckConstraint("CK_detalle_cotizaciones_subtotal_cuadra", "subtotal_centavos = cantidad * precio_unitario_centavos - descuento_linea_centavos");
                    table.CheckConstraint("CK_detalle_cotizaciones_subtotal_no_negativo", "subtotal_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_detalle_cotizaciones_cotizaciones_cotizacion_id",
                        column: x => x.cotizacion_id,
                        principalTable: "cotizaciones",
                        principalColumn: "cotizacion_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_detalle_cotizaciones_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "historial_estado_cotizaciones",
                columns: table => new
                {
                    historial_estado_cotizacion_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    cotizacion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    estado_anterior = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    estado_nuevo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historial_estado_cotizaciones", x => x.historial_estado_cotizacion_id);
                    table.ForeignKey(
                        name: "FK_historial_estado_cotizaciones_cotizaciones_cotizacion_id",
                        column: x => x.cotizacion_id,
                        principalTable: "cotizaciones",
                        principalColumn: "cotizacion_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_historial_estado_cotizaciones_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cotizaciones_cliente_id",
                table: "cotizaciones",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_cotizaciones_codigo_cotizacion",
                table: "cotizaciones",
                column: "codigo_cotizacion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cotizaciones_estado",
                table: "cotizaciones",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_cotizaciones_fecha",
                table: "cotizaciones",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "IX_cotizaciones_fecha_vencimiento",
                table: "cotizaciones",
                column: "fecha_vencimiento");

            migrationBuilder.CreateIndex(
                name: "IX_cotizaciones_usuario_creador_id",
                table: "cotizaciones",
                column: "usuario_creador_id");

            migrationBuilder.CreateIndex(
                name: "IX_cotizaciones_usuario_ultima_actualizacion_id",
                table: "cotizaciones",
                column: "usuario_ultima_actualizacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_cotizaciones_cotizacion_id",
                table: "detalle_cotizaciones",
                column: "cotizacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_cotizaciones_producto_id",
                table: "detalle_cotizaciones",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_estado_cotizaciones_cotizacion_id",
                table: "historial_estado_cotizaciones",
                column: "cotizacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_estado_cotizaciones_fecha",
                table: "historial_estado_cotizaciones",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "IX_historial_estado_cotizaciones_usuario_id",
                table: "historial_estado_cotizaciones",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "detalle_cotizaciones");

            migrationBuilder.DropTable(
                name: "historial_estado_cotizaciones");

            migrationBuilder.DropTable(
                name: "cotizaciones");
        }
    }
}
