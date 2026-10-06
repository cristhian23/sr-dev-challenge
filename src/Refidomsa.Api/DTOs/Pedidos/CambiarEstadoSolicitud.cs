using System.ComponentModel.DataAnnotations;
using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.DTOs.Pedidos;

public class CambiarEstadoSolicitud : IValidatableObject
{
    [Required]
    public string? NuevoEstado { get; set; }

    [StringLength(1000)]
    public string? Motivo { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NuevoEstado != null && !Enum.GetNames<EstadoPedido>().Contains(NuevoEstado))
        {
            yield return new ValidationResult("El estado debe ser un nombre de estado valido.",
                new[] { nameof(NuevoEstado) });
        }
        if (NuevoEstado == nameof(EstadoPedido.Rechazado) && string.IsNullOrWhiteSpace(Motivo))
        {
            yield return new ValidationResult("Rechazar un pedido requiere un motivo.",
                new[] { nameof(Motivo) });
        }
    }
}
