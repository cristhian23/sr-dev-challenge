namespace Refidomsa.Api.DTOs.Credito;

public class CreditoRespuesta
{
    public Guid DistribuidorId { get; set; }
    public decimal LimiteCredito { get; set; }
    public decimal CreditoConsumido { get; set; }
    public decimal CreditoDisponible { get; set; }
}
