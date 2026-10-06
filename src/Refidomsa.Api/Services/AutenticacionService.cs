using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.DTOs.Autenticacion;
using Refidomsa.Api.Models;
using Refidomsa.Api.Security;

namespace Refidomsa.Api.Services;

public class AutenticacionService
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<Usuario> _passwordHasher;
    private readonly GeneradorToken _generadorToken;

    public AutenticacionService(AppDbContext db, PasswordHasher<Usuario> passwordHasher,
        GeneradorToken generadorToken)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _generadorToken = generadorToken;
    }

    public async Task<LoginRespuesta?> IniciarSesionAsync(LoginSolicitud solicitud,
        CancellationToken cancellationToken)
    {
        string nombreUsuario = solicitud.NombreUsuario.Trim().ToLowerInvariant();
        var usuario = await _db.Usuarios.AsNoTracking()
            .SingleOrDefaultAsync(usuario => usuario.NombreUsuario == nombreUsuario, cancellationToken);
        if (usuario == null)
        {
            return null;
        }

        var resultado = _passwordHasher.VerifyHashedPassword(usuario, usuario.PasswordHash, solicitud.Password);
        if (resultado == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var fechaEmision = DateTimeOffset.UtcNow;
        return new LoginRespuesta
        {
            Token = _generadorToken.Generar(usuario, fechaEmision),
            ExpiraEnUtc = fechaEmision.AddMinutes(ConfiguracionJwt.DuracionMinutos),
            Usuario = new UsuarioAutenticadoRespuesta
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                NombreUsuario = usuario.NombreUsuario,
                Rol = usuario.Rol.ToString(),
                DistribuidorId = usuario.DistribuidorId
            }
        };
    }
}
