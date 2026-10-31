using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddSuppliersBlockAV160 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proveedores",
                columns: table => new
                {
                    proveedor_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    codigo_proveedor = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    nombre_razon_social = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    nombre_normalizado = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    nombre_contacto = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    telefono = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    telefono_normalizado = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    whatsapp = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    whatsapp_normalizado = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    correo = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    correo_normalizado = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    direccion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    documento_identidad = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    documento_identidad_normalizado = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    documento_fiscal = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    documento_fiscal_normalizado = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    notas = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    usuario_creacion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_actualizacion_id = table.Column<int>(type: "INTEGER", nullable: true),
                    motivo_desactivacion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    fecha_desactivacion = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proveedores", x => x.proveedor_id);
                    table.CheckConstraint("CK_proveedores_codigo_obligatorio", "length(trim(codigo_proveedor)) > 0");
                    table.CheckConstraint("CK_proveedores_motivo_inactivo", "estado <> 'INACTIVO' OR length(trim(coalesce(motivo_desactivacion, ''))) > 0");
                    table.CheckConstraint("CK_proveedores_nombre_obligatorio", "length(trim(nombre_razon_social)) > 0");
                    table.ForeignKey(
                        name: "FK_proveedores_usuarios_usuario_actualizacion_id",
                        column: x => x.usuario_actualizacion_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_proveedores_usuarios_usuario_creacion_id",
                        column: x => x.usuario_creacion_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "producto_proveedor",
                columns: table => new
                {
                    producto_proveedor_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_id = table.Column<int>(type: "INTEGER", nullable: false),
                    proveedor_id = table.Column<int>(type: "INTEGER", nullable: false),
                    codigo_producto_proveedor = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    es_proveedor_principal = table.Column<bool>(type: "INTEGER", nullable: false),
                    ultimo_costo_centavos = table.Column<long>(type: "INTEGER", nullable: true),
                    fecha_ultima_compra = table.Column<DateTime>(type: "TEXT", nullable: true),
                    activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    notas = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_producto_proveedor", x => x.producto_proveedor_id);
                    table.CheckConstraint("CK_producto_proveedor_ultimo_costo_no_negativo", "ultimo_costo_centavos IS NULL OR ultimo_costo_centavos >= 0");
                    table.ForeignKey(
                        name: "FK_producto_proveedor_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "producto_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_producto_proveedor_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalTable: "proveedores",
                        principalColumn: "proveedor_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_producto_proveedor_producto_id",
                table: "producto_proveedor",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_producto_proveedor_producto_id_es_proveedor_principal_activo",
                table: "producto_proveedor",
                columns: new[] { "producto_id", "es_proveedor_principal", "activo" },
                unique: true,
                filter: "es_proveedor_principal = 1 AND activo = 1");

            migrationBuilder.CreateIndex(
                name: "IX_producto_proveedor_producto_id_proveedor_id",
                table: "producto_proveedor",
                columns: new[] { "producto_id", "proveedor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_producto_proveedor_proveedor_id",
                table: "producto_proveedor",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_codigo_proveedor",
                table: "proveedores",
                column: "codigo_proveedor",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_correo_normalizado",
                table: "proveedores",
                column: "correo_normalizado");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_documento_fiscal_normalizado",
                table: "proveedores",
                column: "documento_fiscal_normalizado",
                unique: true,
                filter: "documento_fiscal_normalizado IS NOT NULL AND documento_fiscal_normalizado <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_documento_identidad_normalizado",
                table: "proveedores",
                column: "documento_identidad_normalizado",
                unique: true,
                filter: "documento_identidad_normalizado IS NOT NULL AND documento_identidad_normalizado <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_estado",
                table: "proveedores",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_nombre_normalizado",
                table: "proveedores",
                column: "nombre_normalizado");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_telefono_normalizado",
                table: "proveedores",
                column: "telefono_normalizado");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_tipo",
                table: "proveedores",
                column: "tipo");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_usuario_actualizacion_id",
                table: "proveedores",
                column: "usuario_actualizacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_usuario_creacion_id",
                table: "proveedores",
                column: "usuario_creacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_whatsapp_normalizado",
                table: "proveedores",
                column: "whatsapp_normalizado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "producto_proveedor");

            migrationBuilder.DropTable(
                name: "proveedores");
        }
    }
}
