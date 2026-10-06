using System.ComponentModel.DataAnnotations;

namespace Refidomsa.Api.DTOs.Pedidos;

public class CrearLineaPedidoSolicitud : IValidatableObject
{
    [Required]
    public Guid? ProductoId { get; set; }

    [Required]
    public decimal? Galones { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProductoId == Guid.Empty)
        {
            yield return new ValidationResult("El identificador no puede estar vacio.", new[] { nameof(ProductoId) });
        }

        if (Galones.HasValue && (decimal.Round(Galones.Value, 6) != Galones.Value
            || Galones.Value <= -1000000000000m || Galones.Value >= 1000000000000m))
        {
            yield return new ValidationResult("Galones debe caber en decimal(18,6), sin redondeo.",
                new[] { nameof(Galones) });
        }
    }
}
