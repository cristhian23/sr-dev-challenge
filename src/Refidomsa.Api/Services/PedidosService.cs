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

    public async Task<ListaPedidosRespuesta?> ListarAsync(ListaPedidosSolicitud solicitud,
        UsuarioActual usuarioActual, CancellationToken cancellationToken)
    {
        if (usuarioActual.Rol == Rol.Distribuidor && solicitud.DistribuidorId.HasValue
            && solicitud.DistribuidorId != usuarioActual.DistribuidorId)
        {
            return null;
        }

        var consulta = ConsultarVisibles(usuarioActual);
        if (solicitud.DistribuidorId.HasValue)
        {
            consulta = consulta.Where(pedido => pedido.DistribuidorId == solicitud.DistribuidorId.Value);
        }
        if (solicitud.Estado != null)
        {
            var estado = Enum.Parse<EstadoPedido>(solicitud.Estado);
            consulta = consulta.Where(pedido => pedido.Estado == estado);
        }
        var desde = solicitud.ObtenerDesde();
        var hasta = solicitud.ObtenerHasta();
        if (desde.HasValue)
        {
            consulta = consulta.Where(pedido => pedido.FechaCreacion >= desde.Value);
        }
        if (hasta.HasValue)
        {
            consulta = consulta.Where(pedido => pedido.FechaCreacion < hasta.Value);
        }

        // La solicitud HTTP valida este limite; checked evita overflow tambien ante llamadas internas.
        var desplazamiento = checked((solicitud.Pagina - 1) * solicitud.TamanoPagina);
        var total = await consulta.CountAsync(cancellationToken);
        var items = await consulta.OrderByDescending(pedido => pedido.FechaCreacion)
            .ThenByDescending(pedido => pedido.Id).Skip(desplazamiento).Take(solicitud.TamanoPagina)
            .Select(pedido => new PedidoResumenRespuesta
            {
                Id = pedido.Id,
                DistribuidorId = pedido.DistribuidorId,
                NombreDistribuidor = _db.Distribuidores.Where(distribuidor => distribuidor.Id == pedido.DistribuidorId)
                    .Select(distribuidor => distribuidor.Nombre).First(),
                FechaEntrega = pedido.FechaEntrega,
                FechaCreacion = pedido.FechaCreacion,
                FechaCambioEstado = pedido.FechaCambioEstado,
                Total = pedido.Total,
                Estado = pedido.Estado
            }).ToListAsync(cancellationToken);
        return new ListaPedidosRespuesta
        {
            Items = items,
            Pagina = solicitud.Pagina,
            TamanoPagina = solicitud.TamanoPagina,
            TotalRegistros = total
        };
    }

    public async Task<PedidoDetalleRespuesta?> ObtenerAsync(Guid id, UsuarioActual usuarioActual,
        CancellationToken cancellationToken)
    {
        return await ConsultarVisibles(usuarioActual).Where(pedido => pedido.Id == id)
            .Select(pedido => new PedidoDetalleRespuesta
            {
                Id = pedido.Id,
                DistribuidorId = pedido.DistribuidorId,
                NombreDistribuidor = _db.Distribuidores.Where(distribuidor => distribuidor.Id == pedido.DistribuidorId)
                    .Select(distribuidor => distribuidor.Nombre).First(),
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
            }).SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<Pedido> ConsultarVisibles(UsuarioActual usuarioActual)
    {
        var consulta = _db.Pedidos.AsNoTracking();
        if (usuarioActual.Rol == Rol.Distribuidor)
        {
            consulta = consulta.Where(pedido => pedido.DistribuidorId == usuarioActual.DistribuidorId);
        }
        return consulta;
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
