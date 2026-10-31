using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddFinancialAnalyticsBlockDV180 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "costo_neto_unitario_centavos",
                table: "recepcion_compra_detalles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "descuento_asignado_centavos",
                table: "recepcion_compra_detalles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "categoria_gasto_id",
                table: "gastos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metodo_pago",
                table: "gastos",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referencia",
                table: "gastos",
                type: "TEXT",
                maxLength: 180,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "categorias_gasto",
                columns: table => new
                {
                    categoria_gasto_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    activa = table.Column<bool>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categorias_gasto", x => x.categoria_gasto_id);
                    table.CheckConstraint("CK_categorias_gasto_nombre_obligatorio", "length(trim(nombre)) > 0");
                });

            migrationBuilder.Sql(
                """
                INSERT INTO categorias_gasto (nombre, activa, fecha_creacion)
                VALUES
                    ('Sin categoría', 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                    ('Insumos', 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                    ('Mantenimiento', 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                    ('Servicios', 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                    ('Transporte', 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                    ('Otros', 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

                UPDATE detalle_ventas
                SET origen_costo = 'COINCIDE_ULTIMA_RECEPCION'
                WHERE origen_costo = 'COMPRA_RECEPCION';

                UPDATE detalle_cambios_venta
                SET origen_costo = 'COINCIDE_ULTIMA_RECEPCION'
                WHERE origen_costo = 'COMPRA_RECEPCION';

                INSERT INTO configuracion_sistema (clave, valor, descripcion, fecha_modificacion)
                SELECT
                    'analytics.snapshots_recepciones_desde_utc',
                    strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                    'Fecha UTC desde la que la aplicacion captura costo neto y descuento exacto por recepcion.',
                    strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                WHERE NOT EXISTS (
                    SELECT 1 FROM configuracion_sistema
                    WHERE clave = 'analytics.snapshots_recepciones_desde_utc'
                );
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_recepcion_compra_detalles_costo_neto_no_negativo",
                table: "recepcion_compra_detalles",
                sql: "costo_neto_unitario_centavos IS NULL OR costo_neto_unitario_centavos >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_recepcion_compra_detalles_descuento_asignado_no_negativo",
                table: "recepcion_compra_detalles",
                sql: "descuento_asignado_centavos IS NULL OR descuento_asignado_centavos >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_gastos_categoria_gasto_id",
                table: "gastos",
                column: "categoria_gasto_id");

            migrationBuilder.CreateIndex(
                name: "IX_categorias_gasto_nombre",
                table: "categorias_gasto",
                column: "nombre",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_gastos_categorias_gasto_categoria_gasto_id",
                table: "gastos",
                column: "categoria_gasto_id",
                principalTable: "categorias_gasto",
                principalColumn: "categoria_gasto_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE detalle_ventas
                SET origen_costo = 'COMPRA_RECEPCION'
                WHERE origen_costo = 'COINCIDE_ULTIMA_RECEPCION';

                UPDATE detalle_cambios_venta
                SET origen_costo = 'COMPRA_RECEPCION'
                WHERE origen_costo = 'COINCIDE_ULTIMA_RECEPCION';

                DELETE FROM configuracion_sistema
                WHERE clave = 'analytics.snapshots_recepciones_desde_utc';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_gastos_categorias_gasto_categoria_gasto_id",
                table: "gastos");

            migrationBuilder.DropTable(
                name: "categorias_gasto");

            migrationBuilder.DropCheckConstraint(
                name: "CK_recepcion_compra_detalles_costo_neto_no_negativo",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_recepcion_compra_detalles_descuento_asignado_no_negativo",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropIndex(
                name: "IX_gastos_categoria_gasto_id",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "costo_neto_unitario_centavos",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropColumn(
                name: "descuento_asignado_centavos",
                table: "recepcion_compra_detalles");

            migrationBuilder.DropColumn(
                name: "categoria_gasto_id",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "metodo_pago",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "referencia",
                table: "gastos");
        }
    }
}
