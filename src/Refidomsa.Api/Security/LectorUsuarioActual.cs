using System.Security.Claims;
using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.Security;

public class LectorUsuarioActual
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LectorUsuarioActual(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public UsuarioActual Obtener()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal == null || !TryCrear(principal, out var usuarioActual))
        {
            throw new UnauthorizedAccessException("Identidad no valida.");
        }

        return usuarioActual!;
    }

    public static bool TryCrear(ClaimsPrincipal principal, out UsuarioActual? usuarioActual)
    {
        usuarioActual = null;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var sujetos = principal.FindAll("sub").ToArray();
        var roles = principal.FindAll("role").ToArray();
        var distribuidores = principal.FindAll("distribuidorId").ToArray();
        if (sujetos.Length != 1 || !Guid.TryParse(sujetos[0].Value, out var usuarioId)
            || usuarioId == Guid.Empty || roles.Length != 1)
        {
            return false;
        }

        if (roles[0].Value == "Operador" && distribuidores.Length == 0)
        {
            usuarioActual = new UsuarioActual(Rol.Operador, null);
            return true;
        }

        if (roles[0].Value == "Distribuidor" && distribuidores.Length == 1
            && Guid.TryParse(distribuidores[0].Value, out var distribuidorId)
            && distribuidorId != Guid.Empty)
        {
            usuarioActual = new UsuarioActual(Rol.Distribuidor, distribuidorId);
            return true;
        }

        return false;
    }
}
