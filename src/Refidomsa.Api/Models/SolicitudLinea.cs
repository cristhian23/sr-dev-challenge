namespace Refidomsa.Api.Models;

// Entrada interna con el producto del catalogo. El DTO HTTP solo enviara su ID y galones.
public class SolicitudLinea
{
    public Producto Producto { get; }
    public decimal Galones { get; }

    public SolicitudLinea(Producto producto, decimal galones)
    {
        Producto = producto;
        Galones = galones;
    }
}
