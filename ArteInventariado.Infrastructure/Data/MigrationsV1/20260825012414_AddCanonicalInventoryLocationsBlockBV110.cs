using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArteInventariado.Infrastructure.Data.MigrationsV1
{
    /// <inheritdoc />
    public partial class AddCanonicalInventoryLocationsBlockBV110 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reservas_inventario_pedido_detalle_pedido_personalizado_id_producto_id",
                table: "reservas_inventario_pedido");

            migrationBuilder.DropIndex(
                name: "IX_producto_skus_producto_id",
                table: "producto_skus");

            migrationBuilder.AddColumn<int>(
                name: "producto_sku_id",
                table: "reservas_inventario_pedido",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ubicacion",
                table: "reservas_inventario_pedido",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "es_principal",
                table: "producto_skus",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "minimo_exhibicion_override",
                table: "producto_skus",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alertas_reposicion_inventario",
                columns: table => new
                {
                    alerta_reposicion_inventario_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_sku_id = table.Column<int>(type: "INTEGER", nullable: false),
                    estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    pospuesta_hasta = table.Column<DateTime>(type: "TEXT", nullable: true),
                    motivo_descarte = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    usuario_actualizacion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alertas_reposicion_inventario", x => x.alerta_reposicion_inventario_id);
                    table.CheckConstraint("CK_alertas_reposicion_descarte_con_motivo", "estado <> 'DESCARTADA' OR length(trim(motivo_descarte)) > 0");
                    table.ForeignKey(
                        name: "FK_alertas_reposicion_inventario_producto_skus_producto_sku_id",
                        column: x => x.producto_sku_id,
                        principalTable: "producto_skus",
                        principalColumn: "producto_sku_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_alertas_reposicion_inventario_usuarios_usuario_actualizacion_id",
                        column: x => x.usuario_actualizacion_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventario_saldos",
                columns: table => new
                {
                    inventario_saldo_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    producto_sku_id = table.Column<int>(type: "INTEGER", nullable: false),
                    ubicacion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    existencia_fisica = table.Column<int>(type: "INTEGER", nullable: false),
                    reservado = table.Column<int>(type: "INTEGER", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventario_saldos", x => x.inventario_saldo_id);
                    table.CheckConstraint("CK_inventario_saldos_fisico_no_negativo", "existencia_fisica >= 0");
                    table.CheckConstraint("CK_inventario_saldos_reservado_no_negativo", "reservado >= 0");
                    table.CheckConstraint("CK_inventario_saldos_reservado_no_supera_fisico", "reservado <= existencia_fisica");
                    table.CheckConstraint("CK_inventario_saldos_version_positiva", "version > 0");
                    table.ForeignKey(
                        name: "FK_inventario_saldos_producto_skus_producto_sku_id",
                        column: x => x.producto_sku_id,
                        principalTable: "producto_skus",
                        principalColumn: "producto_sku_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movimientos_inventario_ubicacion",
                columns: table => new
                {
                    movimiento_inventario_ubicacion_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    operacion_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    producto_sku_id = table.Column<int>(type: "INTEGER", nullable: false),
                    tipo_operacion = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ubicacion_origen = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ubicacion_destino = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    usuario_id = table.Column<int>(type: "INTEGER", nullable: false),
                    motivo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    nota = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    fecha = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimientos_inventario_ubicacion", x => x.movimiento_inventario_ubicacion_id);
                    table.CheckConstraint("CK_movimientos_inventario_ubicacion_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("CK_movimientos_inventario_ubicacion_transferencia_valida", "tipo_operacion <> 'TRANSFERENCIA' OR (ubicacion_origen IS NOT NULL AND ubicacion_destino IS NOT NULL AND ubicacion_origen <> ubicacion_destino)");
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_ubicacion_producto_skus_producto_sku_id",
                        column: x => x.producto_sku_id,
                        principalTable: "producto_skus",
                        principalColumn: "producto_sku_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_ubicacion_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            // La transicion no inventa una distribucion: todo el stock legado queda en
            // SIN_ASIGNAR del SKU principal. El guard hace fallar toda la migracion si
            // falta un SKU o una reserva activa supera el fisico legado.
            migrationBuilder.Sql(
                """
                UPDATE producto_skus
                SET es_principal = CASE
                    WHEN producto_sku_id = (
                        SELECT MIN(candidate.producto_sku_id)
                        FROM producto_skus AS candidate
                        WHERE candidate.producto_id = producto_skus.producto_id)
                    THEN 1 ELSE 0 END;

                CREATE TEMP TABLE __inventory_migration_guard (
                    ok INTEGER NOT NULL CHECK (ok = 1));

                INSERT INTO __inventory_migration_guard (ok)
                SELECT CASE WHEN
                    EXISTS (
                        SELECT 1 FROM productos AS product
                        WHERE NOT EXISTS (
                            SELECT 1 FROM producto_skus AS sku
                            WHERE sku.producto_id = product.producto_id))
                    OR EXISTS (
                        SELECT 1
                        FROM productos AS product
                        WHERE COALESCE((
                            SELECT SUM(reservation.cantidad_reservada - reservation.cantidad_consumida - reservation.cantidad_liberada)
                            FROM reservas_inventario_pedido AS reservation
                            WHERE reservation.producto_id = product.producto_id
                              AND reservation.estado = 'ACTIVA'), 0) > product.stock_actual)
                    THEN 0 ELSE 1 END;

                DROP TABLE __inventory_migration_guard;

                INSERT INTO inventario_saldos
                    (producto_sku_id, ubicacion, existencia_fisica, reservado, version)
                SELECT
                    sku.producto_sku_id,
                    locations.ubicacion,
                    CASE WHEN sku.es_principal = 1 AND locations.ubicacion = 'SIN_ASIGNAR'
                        THEN product.stock_actual ELSE 0 END,
                    CASE WHEN sku.es_principal = 1 AND locations.ubicacion = 'SIN_ASIGNAR'
                        THEN COALESCE((
                            SELECT SUM(reservation.cantidad_reservada - reservation.cantidad_consumida - reservation.cantidad_liberada)
                            FROM reservas_inventario_pedido AS reservation
                            WHERE reservation.producto_id = product.producto_id
                              AND reservation.estado = 'ACTIVA'), 0)
                        ELSE 0 END,
                    1
                FROM producto_skus AS sku
                INNER JOIN productos AS product ON product.producto_id = sku.producto_id
                CROSS JOIN (
                    SELECT 'EXHIBICION' AS ubicacion
                    UNION ALL SELECT 'BODEGA'
                    UNION ALL SELECT 'SIN_ASIGNAR') AS locations;

                UPDATE reservas_inventario_pedido
                SET producto_sku_id = (
                        SELECT sku.producto_sku_id
                        FROM producto_skus AS sku
                        WHERE sku.producto_id = reservas_inventario_pedido.producto_id
                          AND sku.es_principal = 1),
                    ubicacion = 'SIN_ASIGNAR';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_reservas_inventario_pedido_detalle_pedido_personalizado_id_producto_sku_id_ubicacion",
                table: "reservas_inventario_pedido",
                columns: new[] { "detalle_pedido_personalizado_id", "producto_sku_id", "ubicacion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservas_inventario_pedido_producto_sku_id",
                table: "reservas_inventario_pedido",
                column: "producto_sku_id");

            migrationBuilder.CreateIndex(
                name: "IX_producto_skus_producto_id",
                table: "producto_skus",
                column: "producto_id",
                unique: true,
                filter: "es_principal = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_producto_skus_minimo_exhibicion_no_negativo",
                table: "producto_skus",
                sql: "minimo_exhibicion_override IS NULL OR minimo_exhibicion_override >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_alertas_reposicion_inventario_producto_sku_id",
                table: "alertas_reposicion_inventario",
                column: "producto_sku_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alertas_reposicion_inventario_usuario_actualizacion_id",
                table: "alertas_reposicion_inventario",
                column: "usuario_actualizacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventario_saldos_producto_sku_id_ubicacion",
                table: "inventario_saldos",
                columns: new[] { "producto_sku_id", "ubicacion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_ubicacion_operacion_id",
                table: "movimientos_inventario_ubicacion",
                column: "operacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_ubicacion_producto_sku_id_fecha",
                table: "movimientos_inventario_ubicacion",
                columns: new[] { "producto_sku_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_ubicacion_usuario_id",
                table: "movimientos_inventario_ubicacion",
                column: "usuario_id");

            migrationBuilder.AddForeignKey(
                name: "FK_reservas_inventario_pedido_producto_skus_producto_sku_id",
                table: "reservas_inventario_pedido",
                column: "producto_sku_id",
                principalTable: "producto_skus",
                principalColumn: "producto_sku_id",
                onDelete: ReferentialAction.Restrict);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservas_inventario_pedido_producto_skus_producto_sku_id",
                table: "reservas_inventario_pedido");

            migrationBuilder.DropTable(
                name: "alertas_reposicion_inventario");

            migrationBuilder.DropTable(
                name: "inventario_saldos");

            migrationBuilder.DropTable(
                name: "movimientos_inventario_ubicacion");

            migrationBuilder.DropIndex(
                name: "IX_reservas_inventario_pedido_detalle_pedido_personalizado_id_producto_sku_id_ubicacion",
                table: "reservas_inventario_pedido");

            migrationBuilder.DropIndex(
                name: "IX_reservas_inventario_pedido_producto_sku_id",
                table: "reservas_inventario_pedido");

            migrationBuilder.DropIndex(
                name: "IX_producto_skus_producto_id",
                table: "producto_skus");

            migrationBuilder.DropCheckConstraint(
                name: "CK_producto_skus_minimo_exhibicion_no_negativo",
                table: "producto_skus");

            migrationBuilder.DropColumn(
                name: "producto_sku_id",
                table: "reservas_inventario_pedido");

            migrationBuilder.DropColumn(
                name: "ubicacion",
                table: "reservas_inventario_pedido");

            migrationBuilder.DropColumn(
                name: "es_principal",
                table: "producto_skus");

            migrationBuilder.DropColumn(
                name: "minimo_exhibicion_override",
                table: "producto_skus");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_inventario_pedido_detalle_pedido_personalizado_id_producto_id",
                table: "reservas_inventario_pedido",
                columns: new[] { "detalle_pedido_personalizado_id", "producto_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_producto_skus_producto_id",
                table: "producto_skus",
                column: "producto_id");
        }
    }
}
