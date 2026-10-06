using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Refidomsa.Api.DTOs.Autenticacion;
using Refidomsa.Api.Services;

namespace Refidomsa.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AutenticacionController : ControllerBase
{
    private readonly AutenticacionService _autenticacion;

    public AutenticacionController(AutenticacionService autenticacion)
    {
        _autenticacion = autenticacion;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    // Aplicar no-store tambien antes del rechazo automatico de entradas invalidas.
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None, Order = -3000)]
    public async Task<ActionResult<LoginRespuesta>> Login(LoginSolicitud solicitud,
        CancellationToken cancellationToken)
    {
        var respuesta = await _autenticacion.IniciarSesionAsync(solicitud, cancellationToken);
        if (respuesta == null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Credenciales inválidas",
                detail: "Nombre de usuario o contraseña incorrectos.");
        }

        return Ok(respuesta);
    }
}
