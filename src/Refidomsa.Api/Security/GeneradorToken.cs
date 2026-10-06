using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;

namespace Refidomsa.Api.Security;

public class GeneradorToken
{
    private readonly ConfiguracionJwt _configuracion;

    public GeneradorToken(ConfiguracionJwt configuracion)
    {
        configuracion.Validar();
        _configuracion = configuracion;
    }

    public string Generar(Usuario usuario, DateTimeOffset fechaEmision)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = usuario.Id.ToString(),
            ["name"] = usuario.Nombre,
            ["role"] = usuario.Rol.ToString()
        };
        if (usuario.Rol == Rol.Distribuidor)
        {
            claims["distribuidorId"] = usuario.DistribuidorId!.Value.ToString();
        }

        var fechaUtc = fechaEmision.UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _configuracion.Emisor,
            Audience = _configuracion.Audiencia,
            Claims = claims,
            IssuedAt = fechaUtc,
            NotBefore = fechaUtc,
            Expires = fechaUtc.AddMinutes(ConfiguracionJwt.DuracionMinutos),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuracion.Clave)),
                SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
