using Refidomsa.Api.Exceptions;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.Rules;

public static class ReglasCredito
{
    public static decimal CalcularDisponible(Guid distribuidorId, decimal limite, IEnumerable<Pedido> pedidos)
    {
        ArgumentNullException.ThrowIfNull(pedidos);
        if (distribuidorId == Guid.Empty || limite < 0)
        {
            throw new ReglaNegocioException("credito_invalido", "El distribuidor y su limite deben ser validos.");
        }

        var pedidosQueConsumenCredito = pedidos.Where(pedido =>
            pedido.DistribuidorId == distribuidorId
            && (pedido.Estado == EstadoPedido.Pendiente || pedido.Estado == EstadoPedido.Aprobado));
        decimal creditoConsumido = pedidosQueConsumenCredito.Sum(pedido => pedido.Total);
        return limite - creditoConsumido;
    }
}
