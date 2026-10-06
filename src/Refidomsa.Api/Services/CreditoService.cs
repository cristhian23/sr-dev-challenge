using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.DTOs.Credito;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Rules;
using Refidomsa.Api.Security;

namespace Refidomsa.Api.Services;

public class CreditoService
{
    private readonly AppDbContext _db;

    public CreditoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CreditoRespuesta?> ObtenerAsync(Guid distribuidorId, UsuarioActual usuarioActual,
        CancellationToken cancellationToken)
    {
        if (usuarioActual.Rol != Rol.Operador && usuarioActual.DistribuidorId != distribuidorId)
        {
            return null;
        }

        var credito = await _db.Distribuidores.AsNoTracking()
            .Where(distribuidor => distribuidor.Id == distribuidorId)
            .Select(distribuidor => new
            {
                distribuidor.Id,
                distribuidor.LimiteCredito,
                Consumido = _db.Pedidos.Where(pedido => pedido.DistribuidorId == distribuidor.Id
                    && (pedido.Estado == EstadoPedido.Pendiente || pedido.Estado == EstadoPedido.Aprobado))
                    .Sum(pedido => (decimal?)pedido.Total) ?? 0m
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (credito == null)
        {
            return null;
        }

        return new CreditoRespuesta
        {
            DistribuidorId = credito.Id,
            LimiteCredito = credito.LimiteCredito,
            CreditoConsumido = credito.Consumido,
            CreditoDisponible = ReglasCredito.CalcularDisponible(credito.LimiteCredito, credito.Consumido)
        };
    }
}
