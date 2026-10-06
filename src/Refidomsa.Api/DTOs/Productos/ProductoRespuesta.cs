namespace Refidomsa.Api.DTOs.Productos;

public class ProductoRespuesta
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal PrecioPorGalon { get; set; }
}
