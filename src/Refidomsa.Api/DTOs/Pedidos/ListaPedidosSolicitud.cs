using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.DTOs.Pedidos;

public class ListaPedidosSolicitud : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int Pagina { get; set; } = 1;

    [Range(1, 100)]
    public int TamanoPagina { get; set; } = 10;

    public string? Estado { get; set; }
    public Guid? DistribuidorId { get; set; }
    public string? Desde { get; set; }
    public string? Hasta { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Pagina >= 1 && TamanoPagina >= 1 && (long)(Pagina - 1) * TamanoPagina > int.MaxValue)
        {
            yield return new ValidationResult("El desplazamiento de pagina excede el limite permitido.",
                new[] { nameof(Pagina) });
        }
        if (DistribuidorId == Guid.Empty)
        {
            yield return new ValidationResult("El identificador del distribuidor no puede estar vacio.",
                new[] { nameof(DistribuidorId) });
        }
        if (Estado != null && !Enum.GetNames<EstadoPedido>().Contains(Estado))
        {
            yield return new ValidationResult("El estado debe ser un nombre de estado valido.",
                new[] { nameof(Estado) });
        }
        var desdeValido = IntentarFecha(Desde, out var desde);
        var hastaValido = IntentarFecha(Hasta, out var hasta);
        if (!desdeValido)
        {
            yield return new ValidationResult("Desde debe ser una fecha ISO 8601 con offset explicito.",
                new[] { nameof(Desde) });
        }
        if (!hastaValido)
        {
            yield return new ValidationResult("Hasta debe ser una fecha ISO 8601 con offset explicito.",
                new[] { nameof(Hasta) });
        }
        if (desdeValido && hastaValido && desde.HasValue && hasta.HasValue && desde >= hasta)
        {
            yield return new ValidationResult("Desde debe ser anterior a hasta.",
                new[] { nameof(Desde), nameof(Hasta) });
        }
    }

    public DateTimeOffset? ObtenerDesde()
    {
        return Desde == null ? null : DateTimeOffset.Parse(Desde, CultureInfo.InvariantCulture);
    }

    public DateTimeOffset? ObtenerHasta()
    {
        return Hasta == null ? null : DateTimeOffset.Parse(Hasta, CultureInfo.InvariantCulture);
    }

    private static bool IntentarFecha(string? texto, out DateTimeOffset? fecha)
    {
        fecha = null;
        if (texto == null)
        {
            return true;
        }
        // El binding DateTimeOffset admite fechas sin zona: aqui no inferimos la zona del servidor.
        if (!Regex.IsMatch(texto, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            || !DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor))
        {
            return false;
        }
        fecha = valor;
        return true;
    }
}
