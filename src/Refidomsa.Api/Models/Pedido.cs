using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Rules;
using Refidomsa.Api.Security;

namespace Refidomsa.Api.Models;

public class Pedido
{
    public Guid Id { get; }
    public Guid DistribuidorId { get; }
    public DateTimeOffset FechaEntrega { get; }
    public DateTimeOffset FechaCreacion { get; }
    public DateTimeOffset FechaCambioEstado { get; private set; }
    public IReadOnlyList<LineaPedido> Lineas { get; }
    public decimal Total { get; }
    public EstadoPedido Estado { get; private set; }
    public string? MotivoRechazo { get; private set; }

    private Pedido(Guid distribuidorId, DateTimeOffset fechaEntrega,
        DateTimeOffset fechaCreacion, LineaPedido[] lineas)
    {
        Id = Guid.NewGuid();
        DistribuidorId = distribuidorId;
        FechaEntrega = fechaEntrega.ToUniversalTime();
        FechaCreacion = fechaCreacion.ToUniversalTime();
        FechaCambioEstado = FechaCreacion;
        Estado = EstadoPedido.Pendiente;
        Lineas = Array.AsReadOnly(lineas);
        Total = lineas.Sum(linea => linea.Subtotal);
    }

    public static Pedido Crear(UsuarioActual usuarioActual, DateTimeOffset fechaEntrega,
        DateTimeOffset fechaCreacion, IEnumerable<SolicitudLinea> solicitudes, decimal creditoDisponible)
    {
        ArgumentNullException.ThrowIfNull(solicitudes);
        ReglasPedido.ValidarUsuarioParaCrear(usuarioActual);

        // Basta leer cinco entradas para rechazar cualquier pedido de mas de cuatro lineas.
        var entradas = solicitudes.Take(5).ToArray();
        ReglasPedido.ValidarLineas(entradas);
        ReglasPedido.ValidarFechaEntrega(fechaEntrega, fechaCreacion);

        var lineas = entradas.Select(entrada => new LineaPedido(
            entrada.Producto.Id,
            entrada.Producto.Nombre,
            entrada.Galones,
            entrada.Producto.PrecioPorGalon)).ToArray();

        var pedido = new Pedido(usuarioActual.DistribuidorId.GetValueOrDefault(),
            fechaEntrega, fechaCreacion, lineas);
        ReglasPedido.ValidarCredito(pedido.Total, creditoDisponible);
        return pedido;
    }

    public void CambiarEstado(EstadoPedido nuevoEstado, UsuarioActual usuarioActual,
        DateTimeOffset fechaCambio, string? motivo = null)
    {
        ReglasPedido.ValidarCambioEstado(this, nuevoEstado, usuarioActual, fechaCambio, motivo);

        // Solo modificamos el pedido despues de comprobar todas las reglas.
        Estado = nuevoEstado;
        FechaCambioEstado = fechaCambio.ToUniversalTime();
        MotivoRechazo = null;
        if (nuevoEstado == EstadoPedido.Rechazado)
        {
            MotivoRechazo = motivo?.Trim();
        }
    }
}
