using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class InitialCreateV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categorias",
                columns: table => new
                {
                    categoria_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true),
                    activa = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_modificacion = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categorias", x => x.categoria_id);
                });

            migrationBuilder.CreateTable(
                name: "configuracion_sistema",
                columns: table => new
                {
                    configuracion_sistema_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    clave = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    valor = table.Column<string>(type: "TEXT", nullable: false),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true),
                    fecha_modificacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuracion_sistema", x => x.configuracion_sistema_id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre_completo = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    nombre_usuario = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    rol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ultimo_login = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.usuario_id);
                });

            migrationBuilder.CreateTable(
                name: "productos",
                columns: table => new
                {
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_interno = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    nombre = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true),
                    categoria_id = table.Column<int>(type: "INTEGER", nullable: false),
                    imagen_local_path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    precio_compra_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    precio_venta_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    stock_actual = table.Column<int>(type: "INTEGER", nullable: false),
                    stock_minimo = table.Column<int>(type: "INTEGER", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_modificacion = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productos", x => x.producto_id);
                    table.CheckConstraint("CK_productos_precio_compra_no_negativo", "precio_compra_centavos >= 0");
                    table.CheckConstraint("CK_productos_precio_venta_no_negativo", "precio_venta_centavos >= 0");
                    table.CheckConstraint("CK_productos_stock_actual_no_negativo", "stock_actual >= 0");
                    table.CheckConstraint("CK_productos_stock_minimo_no_negativo", "stock_minimo >= 0");
                    table.ForeignKey(
                        name: "FK_productos_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "categorias",
                        principalColumn: "categoria_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bitacora",
                columns: table => new
                {
                    bitacora_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: true),
                    tipo_accion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    entidad = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    entidad_id = table.Column<int>(type: "INTEGER", nullable: true),
                    descripcion = table.Column<string>(type: "TEXT", nullable: false),
                    datos_antes = table.Column<string>(type: "TEXT", nullable: true),
                    datos_despues = table.Column<string>(type: "TEXT", nullable: true),
                    fecha_accion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bitacora", x => x.bitacora_id);
                    table.ForeignKey(
                        name: "FK_bitacora_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "cajas",
                columns: table => new
                {
                    caja_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    fecha_operacion = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    fecha_apertura = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_cierre = table.Column<DateTime>(type: "TEXT", nullable: true),
                    monto_inicial_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    monto_cierre_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    usuario_apertura_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_cierre_id = table.Column<int>(type: "INTEGER", nullable: true),
                    observaciones = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cajas", x => x.caja_id);
                    table.CheckConstraint("CK_cajas_monto_cierre_no_negativo", "monto_cierre_centavos >= 0");
                    table.CheckConstraint("CK_cajas_monto_inicial_no_negativo", "monto_inicial_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_cajas_usuarios_usuario_apertura_id",
                        column: x => x.usuario_apertura_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cajas_usuarios_usuario_cierre_id",
                        column: x => x.usuario_cierre_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "importaciones_excel",
                columns: table => new
                {
                    importacion_excel_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre_archivo = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ruta_archivo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha_importacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    filas_leidas = table.Column<int>(type: "INTEGER", nullable: false),
                    filas_importadas = table.Column<int>(type: "INTEGER", nullable: false),
                    filas_con_error = table.Column<int>(type: "INTEGER", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    resumen_errores = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_importaciones_excel", x => x.importacion_excel_id);
                    table.CheckConstraint("CK_importaciones_excel_filas_error_no_negativo", "filas_con_error >= 0");
                    table.CheckConstraint("CK_importaciones_excel_filas_importadas_no_negativo", "filas_importadas >= 0");
                    table.CheckConstraint("CK_importaciones_excel_filas_leidas_no_negativo", "filas_leidas >= 0");
                    table.ForeignKey(
                        name: "FK_importaciones_excel_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gastos",
                columns: table => new
                {
                    gasto_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    concepto = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    fecha_gasto = table.Column<DateTime>(type: "TEXT", nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gastos", x => x.gasto_id);
                    table.CheckConstraint("CK_gastos_concepto_obligatorio", "length(trim(concepto)) > 0");
                    table.CheckConstraint("CK_gastos_monto_positivo", "monto_centavos > 0");
                    table.ForeignKey(
                        name: "FK_gastos_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gastos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "retiros_caja",
                columns: table => new
                {
                    retiro_caja_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    fecha_retiro = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retiros_caja", x => x.retiro_caja_id);
                    table.CheckConstraint("CK_retiros_caja_monto_positivo", "monto_centavos > 0");
                    table.CheckConstraint("CK_retiros_caja_motivo_obligatorio", "length(trim(motivo)) > 0");
                    table.ForeignKey(
                        name: "FK_retiros_caja_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_retiros_caja_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ventas",
                columns: table => new
                {
                    venta_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    numero_venta = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    caja_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha_venta = table.Column<DateTime>(type: "TEXT", nullable: false),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    total_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    observaciones = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ventas", x => x.venta_id);
                    table.CheckConstraint("CK_ventas_descuento_motivo_obligatorio", "descuento_centavos = 0 OR length(trim(coalesce(descuento_motivo, ''))) > 0");
                    table.CheckConstraint("CK_ventas_descuento_no_negativo", "descuento_centavos >= 0");
                    table.CheckConstraint("CK_ventas_subtotal_no_negativo", "subtotal_centavos >= 0");
                    table.CheckConstraint("CK_ventas_total_no_negativo", "total_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_ventas_cajas_caja_id",
                        column: x => x.caja_id,
                        principalTable: "cajas",
                        principalColumn: "caja_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ventas_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detalle_ventas",
                columns: table => new
                {
                    detalle_venta_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    venta_id = table.Column<int>(type: "INTEGER", nullable: false),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    precio_unitario_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    descuento_motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    total_centavos = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalle_ventas", x => x.detalle_venta_id);
                    table.CheckConstraint("CK_detalle_ventas_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("CK_detalle_ventas_descuento_motivo_obligatorio", "descuento_centavos = 0 OR length(trim(coalesce(descuento_motivo, ''))) > 0");
                    table.CheckConstraint("CK_detalle_ventas_descuento_no_negativo", "descuento_centavos >= 0");
                    table.CheckConstraint("CK_detalle_ventas_precio_unitario_no_negativo", "precio_unitario_centavos >= 0");
                    table.CheckConstraint("CK_detalle_ventas_total_no_negativo", "total_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_detalle_ventas_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalle_ventas_ventas_venta_id",
                        column: x => x.venta_id,
                        principalTable: "ventas",
                        principalColumn: "venta_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movimientos_inventario",
                columns: table => new
                {
                    movimiento_inventario_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    venta_id = table.Column<int>(type: "INTEGER", nullable: true),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    tipo_movimiento = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    stock_anterior = table.Column<int>(type: "INTEGER", nullable: false),
                    stock_nuevo = table.Column<int>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    fecha_movimiento = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimientos_inventario", x => x.movimiento_inventario_id);
                    table.CheckConstraint("CK_movimientos_inventario_cantidad_distinta_cero", "cantidad <> 0");
                    table.CheckConstraint("CK_movimientos_inventario_motivo_obligatorio", "length(trim(motivo)) > 0");
                    table.CheckConstraint("CK_movimientos_inventario_stock_anterior_no_negativo", "stock_anterior >= 0");
                    table.CheckConstraint("CK_movimientos_inventario_stock_nuevo_no_negativo", "stock_nuevo >= 0");
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_ventas_venta_id",
                        column: x => x.venta_id,
                        principalTable: "ventas",
                        principalColumn: "venta_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pagos_venta",
                columns: table => new
                {
                    pago_venta_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    venta_id = table.Column<int>(type: "INTEGER", nullable: false),
                    metodo_pago = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    monto_centavos = table.Column<long>(type: "INTEGER", nullable: false),
                    referencia = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    fecha_pago = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos_venta", x => x.pago_venta_id);
                    table.CheckConstraint("CK_pagos_venta_monto_positivo", "monto_centavos > 0");
                    table.ForeignKey(
                        name: "FK_pagos_venta_ventas_venta_id",
                        column: x => x.venta_id,
                        principalTable: "ventas",
                        principalColumn: "venta_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bitacora_entidad_entidad_id",
                table: "bitacora",
                columns: new[] { "entidad", "entidad_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bitacora_fecha_accion",
                table: "bitacora",
                column: "fecha_accion");

            migrationBuilder.CreateIndex(
                name: "IX_bitacora_tipo_accion",
                table: "bitacora",
                column: "tipo_accion");

            migrationBuilder.CreateIndex(
                name: "IX_bitacora_usuario_id",
                table: "bitacora",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_cajas_estado",
                table: "cajas",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_cajas_fecha_operacion",
                table: "cajas",
                column: "fecha_operacion");

            migrationBuilder.CreateIndex(
                name: "IX_cajas_usuario_apertura_id",
                table: "cajas",
                column: "usuario_apertura_id");

            migrationBuilder.CreateIndex(
                name: "IX_cajas_usuario_cierre_id",
                table: "cajas",
                column: "usuario_cierre_id");

            migrationBuilder.CreateIndex(
                name: "IX_categorias_nombre",
                table: "categorias",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuracion_sistema_clave",
                table: "configuracion_sistema",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_detalle_ventas_producto_id",
                table: "detalle_ventas",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_ventas_venta_id",
                table: "detalle_ventas",
                column: "venta_id");

            migrationBuilder.CreateIndex(
                name: "IX_gastos_caja_id",
                table: "gastos",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_gastos_fecha_gasto",
                table: "gastos",
                column: "fecha_gasto");

            migrationBuilder.CreateIndex(
                name: "IX_gastos_usuario_id",
                table: "gastos",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_importaciones_excel_estado",
                table: "importaciones_excel",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_importaciones_excel_fecha_importacion",
                table: "importaciones_excel",
                column: "fecha_importacion");

            migrationBuilder.CreateIndex(
                name: "IX_importaciones_excel_usuario_id",
                table: "importaciones_excel",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_fecha_movimiento",
                table: "movimientos_inventario",
                column: "fecha_movimiento");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_producto_id",
                table: "movimientos_inventario",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_usuario_id",
                table: "movimientos_inventario",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_venta_id",
                table: "movimientos_inventario",
                column: "venta_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_venta_venta_id",
                table: "pagos_venta",
                column: "venta_id");

            migrationBuilder.CreateIndex(
                name: "IX_productos_categoria_id",
                table: "productos",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "IX_productos_codigo_interno",
                table: "productos",
                column: "codigo_interno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productos_estado",
                table: "productos",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_productos_nombre",
                table: "productos",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "IX_retiros_caja_caja_id",
                table: "retiros_caja",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_retiros_caja_fecha_retiro",
                table: "retiros_caja",
                column: "fecha_retiro");

            migrationBuilder.CreateIndex(
                name: "IX_retiros_caja_usuario_id",
                table: "retiros_caja",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_nombre_usuario",
                table: "usuarios",
                column: "nombre_usuario",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ventas_caja_id",
                table: "ventas",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "IX_ventas_fecha_venta",
                table: "ventas",
                column: "fecha_venta");

            migrationBuilder.CreateIndex(
                name: "IX_ventas_numero_venta",
                table: "ventas",
                column: "numero_venta",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ventas_usuario_id",
                table: "ventas",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bitacora");

            migrationBuilder.DropTable(
                name: "configuracion_sistema");

            migrationBuilder.DropTable(
                name: "detalle_ventas");

            migrationBuilder.DropTable(
                name: "gastos");

            migrationBuilder.DropTable(
                name: "importaciones_excel");

            migrationBuilder.DropTable(
                name: "movimientos_inventario");

            migrationBuilder.DropTable(
                name: "pagos_venta");

            migrationBuilder.DropTable(
                name: "retiros_caja");

            migrationBuilder.DropTable(
                name: "productos");

            migrationBuilder.DropTable(
                name: "ventas");

            migrationBuilder.DropTable(
                name: "categorias");

            migrationBuilder.DropTable(
                name: "cajas");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
