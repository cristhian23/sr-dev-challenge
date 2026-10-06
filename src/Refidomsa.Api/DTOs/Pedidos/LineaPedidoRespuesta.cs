namespace Refidomsa.Api.DTOs.Pedidos;

public class LineaPedidoRespuesta
{
    public Guid ProductoId { get; set; }
    public string NombreProducto { get; set; } = string.Empty;
    public decimal Galones { get; set; }
    public decimal PrecioPorGalon { get; set; }
    public decimal Subtotal { get; set; }
}
