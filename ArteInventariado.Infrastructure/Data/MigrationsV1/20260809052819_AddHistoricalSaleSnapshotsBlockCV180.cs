using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddHistoricalSaleSnapshotsBlockCV180 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "usuario_nombre_snapshot",
                table: "ventas",
                type: "TEXT",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "categoria_id_snapshot",
                table: "detalle_ventas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "categoria_nombre_snapshot",
                table: "detalle_ventas",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "costo_unitario_centavos",
                table: "detalle_ventas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "descuento_general_proporcional_centavos",
                table: "detalle_ventas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_costo",
                table: "detalle_ventas",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "DESCONOCIDO");

            migrationBuilder.AddColumn<string>(
                name: "producto_codigo_snapshot",
                table: "detalle_ventas",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "producto_nombre_snapshot",
                table: "detalle_ventas",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "categoria_id_snapshot",
                table: "detalle_cambios_venta",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "categoria_nombre_snapshot",
                table: "detalle_cambios_venta",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "costo_unitario_centavos",
                table: "detalle_cambios_venta",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_costo",
                table: "detalle_cambios_venta",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "DESCONOCIDO");

            migrationBuilder.AddColumn<string>(
                name: "producto_codigo_snapshot",
                table: "detalle_cambios_venta",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "producto_nombre_snapshot",
                table: "detalle_cambios_venta",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_detalle_ventas_costo_historico_consistente",
                table: "detalle_ventas",
                sql: "(costo_unitario_centavos IS NULL AND origen_costo = 'DESCONOCIDO') OR (costo_unitario_centavos >= 0 AND origen_costo <> 'DESCONOCIDO')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detalle_ventas_descuento_general_no_negativo",
                table: "detalle_ventas",
                sql: "descuento_general_proporcional_centavos IS NULL OR descuento_general_proporcional_centavos >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detalle_cambios_venta_costo_historico_consistente",
                table: "detalle_cambios_venta",
                sql: "(costo_unitario_centavos IS NULL AND origen_costo = 'DESCONOCIDO') OR (costo_unitario_centavos >= 0 AND origen_costo <> 'DESCONOCIDO')");

            migrationBuilder.Sql(
                """
                INSERT INTO configuracion_sistema (clave, valor, descripcion, fecha_modificacion)
                SELECT
                    'analytics.snapshots_ventas_desde_utc',
                    strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                    'Fecha UTC desde la que la aplicacion comenzo a capturar snapshots historicos de ventas.',
                    strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM configuracion_sistema
                    WHERE clave = 'analytics.snapshots_ventas_desde_utc'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM configuracion_sistema WHERE clave = 'analytics.snapshots_ventas_desde_utc';");

            migrationBuilder.DropCheckConstraint(
                name: "CK_detalle_ventas_costo_historico_consistente",
                table: "detalle_ventas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_detalle_ventas_descuento_general_no_negativo",
                table: "detalle_ventas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_detalle_cambios_venta_costo_historico_consistente",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "usuario_nombre_snapshot",
                table: "ventas");

            migrationBuilder.DropColumn(
                name: "categoria_id_snapshot",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "categoria_nombre_snapshot",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "costo_unitario_centavos",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "descuento_general_proporcional_centavos",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "origen_costo",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "producto_codigo_snapshot",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "producto_nombre_snapshot",
                table: "detalle_ventas");

            migrationBuilder.DropColumn(
                name: "categoria_id_snapshot",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "categoria_nombre_snapshot",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "costo_unitario_centavos",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "origen_costo",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "producto_codigo_snapshot",
                table: "detalle_cambios_venta");

            migrationBuilder.DropColumn(
                name: "producto_nombre_snapshot",
                table: "detalle_cambios_venta");
        }
    }
}
