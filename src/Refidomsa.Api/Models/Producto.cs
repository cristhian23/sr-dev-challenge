namespace Refidomsa.Api.Models;

public class Producto
{
    public Guid Id { get; }
    public string Nombre { get; }
    public decimal PrecioPorGalon { get; }

    public Producto(Guid id, string nombre, decimal precioPorGalon)
    {
        Id = id;
        Nombre = nombre;
        PrecioPorGalon = precioPorGalon;
    }
}
