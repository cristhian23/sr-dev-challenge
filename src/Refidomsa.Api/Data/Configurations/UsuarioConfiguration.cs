using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.Data.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("Usuarios", tabla => tabla.HasCheckConstraint("CK_Usuario_RolDistribuidor",
            "([Rol] = 'Distribuidor' AND [DistribuidorId] IS NOT NULL) OR ([Rol] = 'Operador' AND [DistribuidorId] IS NULL)"));
        builder.HasKey(usuario => usuario.Id);
        builder.Property(usuario => usuario.Id).ValueGeneratedNever();
        builder.Property(usuario => usuario.Nombre).HasMaxLength(200).IsRequired();
        builder.Property(usuario => usuario.NombreUsuario).HasMaxLength(100).IsRequired();
        builder.HasIndex(usuario => usuario.NombreUsuario).IsUnique();
        builder.Property(usuario => usuario.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(usuario => usuario.Rol).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Distribuidor>().WithMany().HasForeignKey(usuario => usuario.DistribuidorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
