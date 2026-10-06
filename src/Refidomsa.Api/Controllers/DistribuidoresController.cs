using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Refidomsa.Api.DTOs.Credito;
using Refidomsa.Api.Security;
using Refidomsa.Api.Services;

namespace Refidomsa.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/distribuidores")]
public class DistribuidoresController : ControllerBase
{
    private readonly CreditoService _credito;
    private readonly LectorUsuarioActual _lectorUsuario;

    public DistribuidoresController(CreditoService credito, LectorUsuarioActual lectorUsuario)
    {
        _credito = credito;
        _lectorUsuario = lectorUsuario;
    }

    [HttpGet("{id}/credito")]
    public async Task<ActionResult<CreditoRespuesta>> ObtenerCredito(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            ModelState.AddModelError(nameof(id), "El identificador no puede estar vacio.");
            return ValidationProblem(ModelState);
        }

        var respuesta = await _credito.ObtenerAsync(id, _lectorUsuario.Obtener(), cancellationToken);
        if (respuesta == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Recurso no encontrado", detail: "El distribuidor no existe o no esta disponible.",
                instance: HttpContext.Request.Path);
        }

        return Ok(respuesta);
    }
}
