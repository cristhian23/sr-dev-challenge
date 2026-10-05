using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.Models;

public class Usuario
{
    public Guid Id { get; private set; }
    public string Nombre { get; private set; }
    public string NombreUsuario { get; private set; }
    public string PasswordHash { get; private set; }
    public Rol Rol { get; private set; }
    public Guid? DistribuidorId { get; private set; }

    private Usuario()
    {
        Nombre = string.Empty;
        NombreUsuario = string.Empty;
        PasswordHash = string.Empty;
    }

    public Usuario(Guid id, string nombre, string nombreUsuario, Rol rol, Guid? distribuidorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombreUsuario);
        if (id == Guid.Empty || !Enum.IsDefined(rol))
        {
            throw new ArgumentException("El identificador y rol deben ser validos.");
        }
        if (rol == Rol.Distribuidor && (!distribuidorId.HasValue || distribuidorId == Guid.Empty))
        {
            throw new ArgumentException("Un usuario distribuidor debe pertenecer a un distribuidor.");
        }
        if (rol == Rol.Operador && distribuidorId.HasValue)
        {
            throw new ArgumentException("Un operador no pertenece a un distribuidor.");
        }
        Id = id;
        Nombre = nombre.Trim();
        NombreUsuario = nombreUsuario.Trim().ToLowerInvariant();
        Rol = rol;
        DistribuidorId = distribuidorId;
        PasswordHash = string.Empty;
    }

    public void EstablecerPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }
}
