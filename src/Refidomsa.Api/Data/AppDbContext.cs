using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Models;

namespace Refidomsa.Api.Data;

public class AppDbContext : DbContext
{
    public DbSet<Distribuidor> Distribuidores { get; }
    public DbSet<Producto> Productos { get; }
    public DbSet<Usuario> Usuarios { get; }
    public DbSet<Pedido> Pedidos { get; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        Distribuidores = Set<Distribuidor>();
        Productos = Set<Producto>();
        Usuarios = Set<Usuario>();
        Pedidos = Set<Pedido>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidarPrecisionNumerica();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidarPrecisionNumerica();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidarPrecisionNumerica()
    {
        // SQL Server redondea escalas menores: rechazamos ese cambio silencioso antes de guardar.
        var entradas = ChangeTracker.Entries().Where(entrada =>
            entrada.State == EntityState.Added || entrada.State == EntityState.Modified);
        foreach (var entrada in entradas)
        {
            foreach (var propiedad in entrada.Properties)
            {
                if (propiedad.CurrentValue is decimal valor)
                {
                    int precision = propiedad.Metadata.GetPrecision() ?? 18;
                    int escala = propiedad.Metadata.GetScale() ?? 2;
                    decimal limite = 1;
                    for (int digito = 0; digito < precision - escala; digito++)
                    {
                        limite *= 10;
                    }
                    if (decimal.Round(valor, escala) != valor || valor <= -limite || valor >= limite)
                    {
                        throw new InvalidOperationException(
                            $"{entrada.Metadata.ClrType.Name}.{propiedad.Metadata.Name} no cabe en decimal({precision},{escala}).");
                    }
                }
            }
        }
    }
}
