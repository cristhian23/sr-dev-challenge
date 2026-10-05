using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Security;

namespace Refidomsa.Api.Data;

// Diagnostico local optativo: revierte el pedido y el cambio de precio al terminar.
public class VerificadorPersistencia
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<Usuario> _passwordHasher;

    public VerificadorPersistencia(AppDbContext db, PasswordHasher<Usuario> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task VerificarAsync(string password, CancellationToken cancellationToken = default)
    {
        if (await _db.Distribuidores.CountAsync(cancellationToken) < 3
            || await _db.Productos.CountAsync(cancellationToken) != 4
            || await _db.Usuarios.CountAsync(cancellationToken) < 3)
        {
            throw new InvalidOperationException("Faltan datos semilla. Ejecuta Inicializar primero.");
        }

        var usuarios = await _db.Usuarios.ToArrayAsync(cancellationToken);
        foreach (var usuarioSemilla in usuarios)
        {
            if (_passwordHasher.VerifyHashedPassword(usuarioSemilla, usuarioSemilla.PasswordHash, password) == PasswordVerificationResult.Failed)
            {
                throw new InvalidOperationException("Un hash de usuario no corresponde a SEED_PASSWORD.");
            }
        }
        var usuario = usuarios.Single(actual => actual.NombreUsuario == "distribuidor.norte");

        await using var transaccion = await _db.Database.BeginTransactionAsync(cancellationToken);
        var productos = await _db.Productos.OrderBy(producto => producto.Id).Take(2).ToArrayAsync(cancellationToken);
        decimal precioOriginal = productos[0].PrecioPorGalon;
        var lineas = new[]
        {
            new SolicitudLinea(productos[0], 500.125m),
            new SolicitudLinea(productos[1], 500)
        };
        var fechaCreacion = DateTimeOffset.UtcNow;
        var fechaEntrega = fechaCreacion.AddHours(48);
        if (fechaEntrega.ToOffset(TimeSpan.FromHours(-4)).DayOfWeek == DayOfWeek.Sunday)
        {
            fechaEntrega = fechaEntrega.AddDays(1);
        }

        var usuarioActual = new UsuarioActual(usuario.Rol, usuario.DistribuidorId);
        var pedido = Pedido.Crear(usuarioActual, fechaEntrega, fechaCreacion, lineas, 3_000_000);
        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync(cancellationToken);

        // Simula un cambio real de catalogo sin exponer un setter publico solo para esta prueba.
        _db.Entry(productos[0]).Property(producto => producto.PrecioPorGalon).CurrentValue = precioOriginal + 10;
        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();

        var recuperado = await _db.Pedidos.Include(actual => actual.Lineas)
            .SingleAsync(actual => actual.Id == pedido.Id, cancellationToken);
        var lineaHistorica = recuperado.Lineas.Single(linea => linea.ProductoId == productos[0].Id);
        decimal precioCatalogo = await _db.Productos.Where(producto => producto.Id == productos[0].Id)
            .Select(producto => producto.PrecioPorGalon).SingleAsync(cancellationToken);
        if (recuperado.Lineas.Count != 2 || recuperado.Total != pedido.Total
            || lineaHistorica.PrecioPorGalon != precioOriginal || lineaHistorica.Galones != 500.125m
            || precioCatalogo != precioOriginal + 10
            || recuperado.Estado != EstadoPedido.Pendiente || recuperado.FechaCreacion != fechaCreacion
            || recuperado.FechaEntrega != fechaEntrega)
        {
            throw new InvalidOperationException("La lectura del pedido no conserva sus datos originales.");
        }

        var operador = new UsuarioActual(Rol.Operador, null);
        recuperado.CambiarEstado(EstadoPedido.Aprobado, operador, fechaCreacion.AddMinutes(1));
        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        var aprobado = await _db.Pedidos.SingleAsync(actual => actual.Id == pedido.Id, cancellationToken);
        if (aprobado.Estado != EstadoPedido.Aprobado || aprobado.FechaCambioEstado != fechaCreacion.AddMinutes(1))
        {
            throw new InvalidOperationException("No se conservo el cambio de estado.");
        }

        await transaccion.RollbackAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        if (await _db.Pedidos.AnyAsync(actual => actual.Id == pedido.Id, cancellationToken))
        {
            throw new InvalidOperationException("El diagnostico dejo un pedido en la base de datos.");
        }
        decimal precioRestaurado = await _db.Productos.Where(producto => producto.Id == productos[0].Id)
            .Select(producto => producto.PrecioPorGalon).SingleAsync(cancellationToken);
        if (precioRestaurado != precioOriginal)
        {
            throw new InvalidOperationException("El diagnostico no restauro el precio del catalogo.");
        }
        Console.WriteLine("Persistencia verificada: semilla, hash, pedido, dos lineas, fechas, precio historico, estado y rollback.");
    }
}
