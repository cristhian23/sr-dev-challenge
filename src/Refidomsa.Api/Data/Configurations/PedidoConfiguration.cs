using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Refidomsa.Api.Models;

namespace Refidomsa.Api.Data.Configurations;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.ToTable("Pedidos");
        builder.HasKey(pedido => pedido.Id);
        builder.Property(pedido => pedido.Id).ValueGeneratedNever();
        builder.Property(pedido => pedido.Estado).HasConversion<string>().HasMaxLength(20);
        builder.Property(pedido => pedido.Total).HasPrecision(28, 2);
        builder.Property(pedido => pedido.MotivoRechazo).HasMaxLength(1000);
        builder.HasOne<Distribuidor>().WithMany().HasForeignKey(pedido => pedido.DistribuidorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(pedido => new { pedido.DistribuidorId, pedido.Estado });
        builder.HasIndex(pedido => pedido.FechaCreacion);
        builder.OwnsMany(pedido => pedido.Lineas, lineas =>
        {
            lineas.ToTable("LineasPedido", tabla => tabla.HasCheckConstraint("CK_Linea_Galones", "[Galones] >= 500 AND [Galones] <= 9000"));
            lineas.WithOwner().HasForeignKey("PedidoId");
            lineas.HasKey("PedidoId", nameof(LineaPedido.ProductoId));
            lineas.Property(linea => linea.ProductoId).ValueGeneratedNever();
            lineas.Property(linea => linea.NombreProducto).HasMaxLength(100).IsRequired();
            lineas.Property(linea => linea.Galones).HasPrecision(18, 6);
            lineas.Property(linea => linea.PrecioPorGalon).HasPrecision(18, 6);
            lineas.Property(linea => linea.Subtotal).HasPrecision(28, 2);
            lineas.HasOne<Producto>().WithMany().HasForeignKey(linea => linea.ProductoId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(pedido => pedido.Lineas).HasField("_lineas").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
