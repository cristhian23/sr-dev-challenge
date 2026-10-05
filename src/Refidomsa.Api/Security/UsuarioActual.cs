using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.Security;

// Representa datos de identidad ya verificados; no debe construirse desde el cuerpo HTTP.
public class UsuarioActual
{
    public Rol Rol { get; }
    public Guid? DistribuidorId { get; }

    public UsuarioActual(Rol rol, Guid? distribuidorId)
    {
        Rol = rol;
        DistribuidorId = distribuidorId;
    }
}
