using System.ComponentModel.DataAnnotations;
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

    [HttpGet]
    public async Task<ActionResult<ListaPedidosRespuesta>> Listar([FromQuery] ListaPedidosSolicitud solicitud,
        CancellationToken cancellationToken)
    {
        var respuesta = await _pedidos.ListarAsync(solicitud, _lectorUsuario.Obtener(), cancellationToken);
        if (respuesta == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Recurso no encontrado", detail: "El pedido no existe o no esta disponible.",
                instance: HttpContext.Request.Path);
        }
        return Ok(respuesta);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PedidoDetalleRespuesta>> Obtener(
        [Required] Guid? id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            ModelState.AddModelError(nameof(id), "El identificador del pedido no puede estar vacio.");
            return ValidationProblem(ModelState);
        }
        var respuesta = await _pedidos.ObtenerAsync(id!.Value, _lectorUsuario.Obtener(), cancellationToken);
        if (respuesta == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Recurso no encontrado", detail: "El pedido no existe o no esta disponible.",
                instance: HttpContext.Request.Path);
        }
        return Ok(respuesta);
    }

    [HttpPatch("{id}/estado")]
    public async Task<ActionResult<PedidoDetalleRespuesta>> CambiarEstado([Required] Guid? id,
        CambiarEstadoSolicitud solicitud, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            ModelState.AddModelError(nameof(id), "El identificador del pedido no puede estar vacio.");
            return ValidationProblem(ModelState);
        }
        var respuesta = await _pedidos.CambiarEstadoAsync(id!.Value, solicitud,
            _lectorUsuario.Obtener(), cancellationToken);
        if (respuesta == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Recurso no encontrado", detail: "El pedido no existe o no esta disponible.",
                instance: HttpContext.Request.Path);
        }
        return Ok(respuesta);
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
