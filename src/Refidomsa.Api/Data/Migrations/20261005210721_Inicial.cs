using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Refidomsa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Distribuidores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rnc = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    LimiteCredito = table.Column<decimal>(type: "decimal(28,2)", precision: 28, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Distribuidores", x => x.Id);
                    table.CheckConstraint("CK_Distribuidor_Credito", "[LimiteCredito] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Productos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PrecioPorGalon = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Productos", x => x.Id);
                    table.CheckConstraint("CK_Producto_Precio", "[PrecioPorGalon] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Pedidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DistribuidorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FechaEntrega = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FechaCreacion = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FechaCambioEstado = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(28,2)", precision: 28, scale: 2, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MotivoRechazo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pedidos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pedidos_Distribuidores_DistribuidorId",
                        column: x => x.DistribuidorId,
                        principalTable: "Distribuidores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NombreUsuario = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DistribuidorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                    table.CheckConstraint("CK_Usuario_RolDistribuidor", "([Rol] = 'Distribuidor' AND [DistribuidorId] IS NOT NULL) OR ([Rol] = 'Operador' AND [DistribuidorId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Usuarios_Distribuidores_DistribuidorId",
                        column: x => x.DistribuidorId,
                        principalTable: "Distribuidores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LineasPedido",
                columns: table => new
                {
                    ProductoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NombreProducto = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Galones = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PrecioPorGalon = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(28,2)", precision: 28, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineasPedido", x => new { x.PedidoId, x.ProductoId });
                    table.CheckConstraint("CK_Linea_Galones", "[Galones] >= 500 AND [Galones] <= 9000");
                    table.ForeignKey(
                        name: "FK_LineasPedido_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LineasPedido_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Distribuidores_Rnc",
                table: "Distribuidores",
                column: "Rnc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineasPedido_ProductoId",
                table: "LineasPedido",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_DistribuidorId_Estado",
                table: "Pedidos",
                columns: new[] { "DistribuidorId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_FechaCreacion",
                table: "Pedidos",
                column: "FechaCreacion");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_DistribuidorId",
                table: "Usuarios",
                column: "DistribuidorId");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_NombreUsuario",
                table: "Usuarios",
                column: "NombreUsuario",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineasPedido");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "Pedidos");

            migrationBuilder.DropTable(
                name: "Productos");

            migrationBuilder.DropTable(
                name: "Distribuidores");
        }
    }
}
