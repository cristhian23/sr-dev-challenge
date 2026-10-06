using System.ComponentModel.DataAnnotations;

namespace Refidomsa.Api.DTOs.Pedidos;

public class CrearPedidoSolicitud : IValidatableObject
{
    [Required]
    public DateTimeOffset? FechaEntrega { get; set; }

    [Required]
    public List<CrearLineaPedidoSolicitud?>? Lineas { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lineas != null && Lineas.Any(linea => linea == null))
        {
            yield return new ValidationResult("Las lineas no pueden ser nulas.", new[] { nameof(Lineas) });
        }
    }
}
