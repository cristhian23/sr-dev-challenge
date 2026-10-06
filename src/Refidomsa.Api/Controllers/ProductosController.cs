using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Refidomsa.Api.DTOs.Productos;
using Refidomsa.Api.Services;

namespace Refidomsa.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/productos")]
public class ProductosController : ControllerBase
{
    private readonly ProductosService _productos;

    public ProductosController(ProductosService productos)
    {
        _productos = productos;
    }

    [HttpGet]
    public async Task<ActionResult<List<ProductoRespuesta>>> Listar(CancellationToken cancellationToken)
    {
        return Ok(await _productos.ListarAsync(cancellationToken));
    }
}
