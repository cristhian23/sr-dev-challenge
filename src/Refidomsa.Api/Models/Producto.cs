namespace Refidomsa.Api.Models;

public class Producto
{
    public Guid Id { get; private set; }
    public string Nombre { get; private set; }
    public decimal PrecioPorGalon { get; private set; }

    private Producto()
    {
        Nombre = string.Empty;
    }

    public Producto(Guid id, string nombre, decimal precioPorGalon)
    {
        Id = id;
        Nombre = nombre;
        PrecioPorGalon = precioPorGalon;
    }
}
