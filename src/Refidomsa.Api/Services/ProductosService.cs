using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.DTOs.Productos;

namespace Refidomsa.Api.Services;

public class ProductosService
{
    private readonly AppDbContext _db;

    public ProductosService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ProductoRespuesta>> ListarAsync(CancellationToken cancellationToken)
    {
        return await _db.Productos.AsNoTracking()
            .OrderBy(producto => producto.Nombre).ThenBy(producto => producto.Id)
            .Select(producto => new ProductoRespuesta
            {
                Id = producto.Id,
                Nombre = producto.Nombre,
                PrecioPorGalon = producto.PrecioPorGalon
            })
            .ToListAsync(cancellationToken);
    }
}
