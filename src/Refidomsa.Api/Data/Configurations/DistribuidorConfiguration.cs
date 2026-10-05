using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Refidomsa.Api.Models;

namespace Refidomsa.Api.Data.Configurations;

public class DistribuidorConfiguration : IEntityTypeConfiguration<Distribuidor>
{
    public void Configure(EntityTypeBuilder<Distribuidor> builder)
    {
        builder.ToTable("Distribuidores", tabla => tabla.HasCheckConstraint("CK_Distribuidor_Credito", "[LimiteCredito] >= 0"));
        builder.HasKey(distribuidor => distribuidor.Id);
        builder.Property(distribuidor => distribuidor.Id).ValueGeneratedNever();
        builder.Property(distribuidor => distribuidor.Nombre).HasMaxLength(200).IsRequired();
        builder.Property(distribuidor => distribuidor.Rnc).HasMaxLength(11).IsRequired();
        builder.HasIndex(distribuidor => distribuidor.Rnc).IsUnique();
        builder.Property(distribuidor => distribuidor.LimiteCredito).HasPrecision(28, 2);
    }
}
