using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Refidomsa.Api.DTOs.Pedidos;
using Refidomsa.Api.Rules;
using Refidomsa.Api.Security;
using Refidomsa.Api.Services;

namespace Refidomsa.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/pedidos")]
public class PedidosController : ControllerBase
{
    private readonly PedidosService _pedidos;
    private readonly LectorUsuarioActual _lectorUsuario;

    public PedidosController(PedidosService pedidos, LectorUsuarioActual lectorUsuario)
    {
        _pedidos = pedidos;
        _lectorUsuario = lectorUsuario;
    }

    [HttpPost]
    [Authorize(Roles = "Distribuidor")]
    public async Task<ActionResult<PedidoDetalleRespuesta>> Crear(CrearPedidoSolicitud solicitud,
        CancellationToken cancellationToken)
    {
        var usuarioActual = _lectorUsuario.Obtener();
        ReglasPedido.ValidarUsuarioParaCrear(usuarioActual);
        var respuesta = await _pedidos.CrearAsync(solicitud, usuarioActual, cancellationToken);
        if (respuesta == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Recurso no encontrado", detail: "El distribuidor no existe o no esta disponible.",
                instance: HttpContext.Request.Path);
        }

        return Created($"/api/pedidos/{respuesta.Id}", respuesta);
    }
}
