using System.Data;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.DTOs.Pedidos;
using Refidomsa.Api.Exceptions;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Rules;
using Refidomsa.Api.Security;

namespace Refidomsa.Api.Services;

public class PedidosService
{
    private readonly AppDbContext _db;

    public PedidosService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PedidoDetalleRespuesta?> CrearAsync(CrearPedidoSolicitud solicitud,
        UsuarioActual usuarioActual, CancellationToken cancellationToken)
    {
        ReglasPedido.ValidarUsuarioParaCrear(usuarioActual);
        var distribuidorId = usuarioActual.DistribuidorId!.Value;
        await using var transaccion = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // El bloqueo por PK es la primera lectura y se conserva hasta terminar la transaccion.
            var distribuidor = await _db.Distribuidores
                .FromSql($"SELECT * FROM Distribuidores WITH (UPDLOCK, HOLDLOCK) WHERE Id = {distribuidorId}")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (distribuidor == null)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                return null;
            }

            var lineas = await ResolverLineasAsync(solicitud, cancellationToken);
            var consumido = await _db.Pedidos.Where(pedido => pedido.DistribuidorId == distribuidorId
                && (pedido.Estado == EstadoPedido.Pendiente || pedido.Estado == EstadoPedido.Aprobado))
                .SumAsync(pedido => (decimal?)pedido.Total, cancellationToken) ?? 0m;
            var disponible = ReglasCredito.CalcularDisponible(distribuidor.LimiteCredito, consumido);
            var pedidoNuevo = Pedido.Crear(usuarioActual, solicitud.FechaEntrega!.Value,
                DateTimeOffset.UtcNow, lineas, disponible);

            _db.Pedidos.Add(pedidoNuevo);
            await _db.SaveChangesAsync(cancellationToken);
            await transaccion.CommitAsync(cancellationToken);
            return CrearRespuesta(pedidoNuevo, distribuidor.Nombre);
        }
        catch
        {
            // Una cancelacion del request no debe cancelar la limpieza de la transaccion.
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<List<SolicitudLinea>> ResolverLineasAsync(CrearPedidoSolicitud solicitud,
        CancellationToken cancellationToken)
    {
        var entradas = solicitud.Lineas!;
        var ids = entradas.Select(linea => linea!.ProductoId!.Value).Distinct().ToArray();
        var productos = await _db.Productos.AsNoTracking().Where(producto => ids.Contains(producto.Id))
            .ToDictionaryAsync(producto => producto.Id, cancellationToken);
        var lineas = new List<SolicitudLinea>();
        foreach (var entrada in entradas)
        {
            if (!productos.TryGetValue(entrada!.ProductoId!.Value, out var producto))
            {
                throw new ReglaNegocioException("producto_invalido", "El producto no existe en el catalogo.");
            }
            lineas.Add(new SolicitudLinea(producto, entrada.Galones!.Value));
        }
        return lineas;
    }

    private static PedidoDetalleRespuesta CrearRespuesta(Pedido pedido, string nombreDistribuidor)
    {
        return new PedidoDetalleRespuesta
        {
            Id = pedido.Id,
            DistribuidorId = pedido.DistribuidorId,
            NombreDistribuidor = nombreDistribuidor,
            FechaEntrega = pedido.FechaEntrega,
            FechaCreacion = pedido.FechaCreacion,
            FechaCambioEstado = pedido.FechaCambioEstado,
            Total = pedido.Total,
            Estado = pedido.Estado,
            MotivoRechazo = pedido.MotivoRechazo,
            Lineas = pedido.Lineas.Select(linea => new LineaPedidoRespuesta
            {
                ProductoId = linea.ProductoId,
                NombreProducto = linea.NombreProducto,
                Galones = linea.Galones,
                PrecioPorGalon = linea.PrecioPorGalon,
                Subtotal = linea.Subtotal
            }).ToList()
        };
    }
}
