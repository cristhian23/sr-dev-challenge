using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Refidomsa.Api.Security;

public class ConfiguracionJwt
{
    public const string Seccion = "Jwt";
    public const int DuracionMinutos = 60;

    public string Emisor { get; set; } = string.Empty;
    public string Audiencia { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;

    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(Emisor) || string.IsNullOrWhiteSpace(Audiencia))
        {
            throw new InvalidOperationException("Configura Jwt__Emisor y Jwt__Audiencia con valores no vacios.");
        }
        if (string.IsNullOrWhiteSpace(Clave) || Encoding.UTF8.GetByteCount(Clave) < 32)
        {
            throw new InvalidOperationException("Configura Jwt__Clave con al menos 32 bytes UTF-8.");
        }
    }

    public TokenValidationParameters CrearParametrosValidacion()
    {
        Validar();
        return new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Clave)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidIssuer = Emisor,
            ValidateAudience = true,
            RequireAudience = true,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidAudience = Audiencia,
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }
}
