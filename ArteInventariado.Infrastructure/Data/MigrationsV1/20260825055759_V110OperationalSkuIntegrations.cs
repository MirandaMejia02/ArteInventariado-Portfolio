using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class V110OperationalSkuIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_detalle_compras_compra_id_producto_id",
                table: "detalle_compras");

            migrationBuilder.AddColumn<string>(
                name: "codigo_sku_snapshot",
                table: "recepcion_compra_detalles",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_variante_snapshot",
                table: "recepcion_compra_detalles",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "producto_sku_id",
                table: "recepcion_compra_detalles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ubicacion_destino",
                table: "recepcion_compra_detalles",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "producto_sku_id",
                table: "mermas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "operacion_id",
                table: "devoluciones",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo_sku_snapshot",
                table: "detalle_pedidos_personalizados",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_variante_snapshot",
                table: "detalle_pedidos_personalizados",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "producto_sku_id",
                table: "detalle_pedidos_personalizados",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo_sku_snapshot",
                table: "detalle_devoluciones",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_variante_snapshot",
                table: "detalle_devoluciones",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "producto_sku_id",
                table: "detalle_devoluciones",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ubicacion_reintegro",
                table: "detalle_devoluciones",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo_sku_snapshot",
                table: "detalle_compras",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_variante_snapshot",
                table: "detalle_compras",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "producto_sku_id",
                table: "detalle_compras",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo_sku_snapshot",
                table: "detalle_cambios_venta",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_variante_snapshot",
                table: "detalle_cambios_venta",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "producto_sku_nuevo_id",
                table: "detalle_cambios_venta",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recepcion_compra_detalles_producto_sku_id",
                table: "recepcion_compra_detalles",
                column: "producto_sku_id");

            migrationBuilder.CreateIndex(
                name: "IX_mermas_producto_sku_id",
                table: "mermas",
                column: "producto_sku_id");

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_operacion_id",
                table: "devoluciones",
                column: "operacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_detalle_pedidos_personalizados_producto_sku_id",
                table: "detalle_pedidos_personalizados",
                column: "producto_sku_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_devoluciones_producto_sku_id",
                table: "detalle_devoluciones",
                column: "producto_sku_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_compras_compra_id_producto_sku_id",
                table: "detalle_compras",
                columns: new[] { "compra_id", "producto_sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_detalle_compras_producto_sku_id",
                table: "detalle_compras",
                column: "producto_sku_id");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_cambios_venta_producto_sku_nuevo_id",
                table: "detalle_cambios_venta",
                column: "producto_sku_nuevo_id");

            migrationBuilder.AddForeignKey(
                name: "FK_detalle_cambios_venta_producto_skus_producto_sku_nuevo_id",
                table: "detalle_cambios_venta",
                column: "producto_sku_nuevo_id",
                principalTable: "producto_skus",
                principalColumn: "producto_sku_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_detalle_compras_producto_skus_producto_sku_id",
                table: "detalle_compras",
                column: "producto_sku_id",
                principalTable: "producto_skus",
                principalColumn: "producto_sku_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_detalle_devoluciones_producto_skus_producto_sku_id",
                table: "detalle_devoluciones",
                column: "producto_sku_id",
                principalTable: "producto_skus",
                principalColumn: "producto_sku_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_detalle_pedidos_personalizados_producto_skus_producto_sku_id",
                table: "detalle_pedidos_personalizados",
                column: "producto_sku_id",
                principalTable: "producto_skus",
                principalColumn: "producto_sku_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_mermas_producto_skus_producto_sku_id",
                table: "mermas",
                column: "producto_sku_id",
                principalTable: "producto_skus",
                principalColumn: "producto_sku_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_recepcion_compra_detalles_producto_skus_producto_sku_id",
                table: "recepcion_compra_detalles",
                column: "producto_sku_id",
                principalTable: "producto_skus",
                principalColumn: "producto_sku_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_detalle_cambios_venta_producto_skus_producto_sku_nuevo_id",
                table: "detalle_cambios_venta");

            migrationBuilder.DropForeignKey(
                name: "FK_detalle_compras_producto_skus_producto_sku_id",
                table: "detalle_compras");

            migrationBuilder.DropForeignKey(
                name: "FK_detalle_devoluciones_producto_skus_producto_sku_id",
                table: "detalle_devoluciones");

            migrationBuilder.DropForeignKey(
                name: "FK_detalle_pedidos_personalizados_producto_skus_producto_sku_id",
                table: "detalle_pedidos_personalizados");

            migrationBuilder.DropForeignKey(
                name: "FK_mermas_producto_skus_producto_sku_id",
                table: "mermas");

            migrationBuilder.DropForeignKey(
                name: "FK_recepcion_compra_detalles_producto_skus_producto_sku_id",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropIndex(
                name: "IX_recepcion_compra_detalles_producto_sku_id",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropIndex(
                name: "IX_mermas_producto_sku_id",
                table: "mermas");

            migrationBuilder.DropIndex(
                name: "IX_devoluciones_operacion_id",
                table: "devoluciones");

            migrationBuilder.DropIndex(
                name: "IX_detalle_pedidos_personalizados_producto_sku_id",
                table: "detalle_pedidos_personalizados");

            migrationBuilder.DropIndex(
                name: "IX_detalle_devoluciones_producto_sku_id",
                table: "detalle_devoluciones");

            migrationBuilder.DropIndex(
                name: "IX_detalle_compras_compra_id_producto_sku_id",
                table: "detalle_compras");

            migrationBuilder.DropIndex(
                name: "IX_detalle_compras_producto_sku_id",
                table: "detalle_compras");

            migrationBuilder.DropIndex(
                name: "IX_detalle_cambios_venta_producto_sku_nuevo_id",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "codigo_sku_snapshot",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropColumn(
                name: "nombre_variante_snapshot",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropColumn(
                name: "producto_sku_id",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropColumn(
                name: "ubicacion_destino",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropColumn(
                name: "producto_sku_id",
                table: "mermas");

            migrationBuilder.DropColumn(
                name: "operacion_id",
                table: "devoluciones");

            migrationBuilder.DropColumn(
                name: "codigo_sku_snapshot",
                table: "detalle_pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "nombre_variante_snapshot",
                table: "detalle_pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "producto_sku_id",
                table: "detalle_pedidos_personalizados");

            migrationBuilder.DropColumn(
                name: "codigo_sku_snapshot",
                table: "detalle_devoluciones");

            migrationBuilder.DropColumn(
                name: "nombre_variante_snapshot",
                table: "detalle_devoluciones");

            migrationBuilder.DropColumn(
                name: "producto_sku_id",
                table: "detalle_devoluciones");

            migrationBuilder.DropColumn(
                name: "ubicacion_reintegro",
                table: "detalle_devoluciones");

            migrationBuilder.DropColumn(
                name: "codigo_sku_snapshot",
                table: "detalle_compras");

            migrationBuilder.DropColumn(
                name: "nombre_variante_snapshot",
                table: "detalle_compras");

            migrationBuilder.DropColumn(
                name: "producto_sku_id",
                table: "detalle_compras");

            migrationBuilder.DropColumn(
                name: "codigo_sku_snapshot",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "nombre_variante_snapshot",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "producto_sku_nuevo_id",
                table: "detalle_cambios_venta");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_compras_compra_id_producto_id",
                table: "detalle_compras",
                columns: new[] { "compra_id", "producto_id" },
                unique: true);
        }
    }
}
