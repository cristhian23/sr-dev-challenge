namespace Refidomsa.Api.Models;

public class LineaPedido
{
    public Guid ProductoId { get; }
    public string NombreProducto { get; }
    public decimal Galones { get; }
    public decimal PrecioPorGalon { get; }
    public decimal Subtotal { get; }

    public LineaPedido(Guid productoId, string nombreProducto, decimal galones, decimal precioPorGalon)
    {
        ProductoId = productoId;
        NombreProducto = nombreProducto;
        Galones = galones;
        PrecioPorGalon = precioPorGalon;
        Subtotal = decimal.Round(galones * precioPorGalon, 2, MidpointRounding.AwayFromZero);
    }
}
