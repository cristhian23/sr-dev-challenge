using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.Data;

public class DatosSemilla
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<Usuario> _passwordHasher;

    public DatosSemilla(AppDbContext db, PasswordHasher<Usuario> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task InicializarAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        await _db.Database.MigrateAsync(cancellationToken);
        await using var transaccion = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var distribuidores = new[]
        {
            new Distribuidor(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Distribuidora Norte (prueba)", "101000001", 3_000_000),
            new Distribuidor(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Distribuidora Sur (prueba)", "101000002", 2_000_000),
            new Distribuidor(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Distribuidora Este (prueba)", "101000003", 1_000_000)
        };
        foreach (var distribuidor in distribuidores)
        {
            if (!await _db.Distribuidores.AnyAsync(actual => actual.Id == distribuidor.Id, cancellationToken))
            {
                _db.Distribuidores.Add(distribuidor);
            }
        }

        var productos = new[]
        {
            new Producto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Gasolina Premium", 290.10m),
            new Producto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "Gasolina Regular", 272.50m),
            new Producto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "Gasoil Óptimo", 239.10m),
            new Producto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), "Gasoil Regular", 221.60m)
        };
        foreach (var producto in productos)
        {
            if (!await _db.Productos.AnyAsync(actual => actual.Id == producto.Id, cancellationToken))
            {
                _db.Productos.Add(producto);
            }
        }

        var usuarios = new[]
        {
            new Usuario(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), "Distribuidor Norte", "distribuidor.norte", Rol.Distribuidor, distribuidores[0].Id),
            new Usuario(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "Distribuidor Sur", "distribuidor.sur", Rol.Distribuidor, distribuidores[1].Id),
            new Usuario(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003"), "Operador Refidomsa", "operador", Rol.Operador, null)
        };
        foreach (var usuario in usuarios)
        {
            if (!await _db.Usuarios.AnyAsync(actual => actual.Id == usuario.Id, cancellationToken))
            {
                usuario.EstablecerPasswordHash(_passwordHasher.HashPassword(usuario, password));
                _db.Usuarios.Add(usuario);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
    }
}
