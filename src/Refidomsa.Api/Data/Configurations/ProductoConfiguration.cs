using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Refidomsa.Api.Models;

namespace Refidomsa.Api.Data.Configurations;

public class ProductoConfiguration : IEntityTypeConfiguration<Producto>
{
    public void Configure(EntityTypeBuilder<Producto> builder)
    {
        builder.ToTable("Productos", tabla => tabla.HasCheckConstraint("CK_Producto_Precio", "[PrecioPorGalon] > 0"));
        builder.HasKey(producto => producto.Id);
        builder.Property(producto => producto.Id).ValueGeneratedNever();
        builder.Property(producto => producto.Nombre).HasMaxLength(100).IsRequired();
        builder.Property(producto => producto.PrecioPorGalon).HasPrecision(18, 6);
    }
}
