using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.DTOs.Pedidos;

public class PedidoDetalleRespuesta
{
    public Guid Id { get; set; }
    public Guid DistribuidorId { get; set; }
    public string NombreDistribuidor { get; set; } = string.Empty;
    public DateTimeOffset FechaEntrega { get; set; }
    public DateTimeOffset FechaCreacion { get; set; }
    public DateTimeOffset FechaCambioEstado { get; set; }
    public decimal Total { get; set; }
    public EstadoPedido Estado { get; set; }
    public string? MotivoRechazo { get; set; }
    public List<LineaPedidoRespuesta> Lineas { get; set; } = new List<LineaPedidoRespuesta>();
}
