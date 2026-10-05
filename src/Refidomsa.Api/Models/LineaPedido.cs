namespace Refidomsa.Api.Models;

public class LineaPedido
{
    public Guid ProductoId { get; private set; }
    public string NombreProducto { get; private set; }
    public decimal Galones { get; private set; }
    public decimal PrecioPorGalon { get; private set; }
    public decimal Subtotal { get; private set; }

    private LineaPedido()
    {
        NombreProducto = string.Empty;
    }

    public LineaPedido(Guid productoId, string nombreProducto, decimal galones, decimal precioPorGalon)
    {
        ProductoId = productoId;
        NombreProducto = nombreProducto;
        Galones = galones;
        PrecioPorGalon = precioPorGalon;
        Subtotal = decimal.Round(galones * precioPorGalon, 2, MidpointRounding.AwayFromZero);
    }
}
