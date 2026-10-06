using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Security;
using Xunit;

namespace Refidomsa.UnitTests;

public class TokenTests
{
    private static ConfiguracionJwt CrearConfiguracion()
    {
        return new ConfiguracionJwt
        {
            Emisor = "Refidomsa.Api",
            Audiencia = "Refidomsa.Client",
            Clave = "01234567890123456789012345678901"
        };
    }

    [Theory]
    [InlineData(Rol.Operador)]
    [InlineData(Rol.Distribuidor)]
    public void Generar_EmiteClaimsDelUsuarioYFechasUtcDeterministas(Rol rol)
    {
        Guid usuarioId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid? distribuidorId = rol == Rol.Distribuidor
            ? Guid.Parse("20000000-0000-0000-0000-000000000001") : null;
        var usuario = new Usuario(usuarioId, "Usuario de prueba", "usuario.prueba", rol, distribuidorId);
        usuario.EstablecerPasswordHash("hash-que-no-debe-aparecer");
        var configuracion = CrearConfiguracion();
        var generador = new GeneradorToken(configuracion);
        var fecha = new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.FromHours(-4));

        string token = generador.Generar(usuario, fecha);
        Assert.Equal(token, generador.Generar(usuario, fecha));
        var jwt = new JsonWebToken(token);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Alg);
        Assert.Equal(configuracion.Emisor, jwt.Issuer);
        Assert.Equal(configuracion.Audiencia, Assert.Single(jwt.Audiences));
        Assert.Equal(usuarioId.ToString(), jwt.Subject);
        Assert.Equal(usuario.Nombre, jwt.GetClaim("name").Value);
        Assert.Equal(rol.ToString(), jwt.GetClaim("role").Value);
        Assert.Equal(fecha.UtcDateTime, jwt.IssuedAt);
        Assert.Equal(fecha.UtcDateTime, jwt.ValidFrom);
        Assert.Equal(fecha.UtcDateTime.AddMinutes(60), jwt.ValidTo);
        Assert.Equal(TimeSpan.FromSeconds(3600), jwt.ValidTo - jwt.ValidFrom);
        Assert.Equal(fecha.ToUnixTimeSeconds(), jwt.GetPayloadValue<long>("iat"));

        if (rol == Rol.Distribuidor)
        {
            Assert.Equal(distribuidorId.ToString(), jwt.GetClaim("distribuidorId").Value);
        }
        else
        {
            Assert.False(jwt.TryGetClaim("distribuidorId", out _));
        }
        string[] claimsEsperados = rol == Rol.Distribuidor
            ? new[] { "sub", "name", "role", "distribuidorId", "iss", "aud", "iat", "nbf", "exp" }
            : new[] { "sub", "name", "role", "iss", "aud", "iat", "nbf", "exp" };
        Assert.Equal(claimsEsperados.OrderBy(nombre => nombre), jwt.Claims.Select(claim => claim.Type).OrderBy(nombre => nombre));
    }

    [Theory]
    [InlineData(Rol.Operador)]
    [InlineData(Rol.Distribuidor)]
    public async Task Generar_FirmaValidableConParametrosCompartidos(Rol rol)
    {
        var configuracion = CrearConfiguracion();
        Guid? distribuidorId = rol == Rol.Distribuidor ? Guid.NewGuid() : null;
        var usuario = new Usuario(Guid.NewGuid(), "Usuario", "usuario", rol, distribuidorId);
        string token = new GeneradorToken(configuracion).Generar(usuario, DateTimeOffset.UtcNow);

        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(
            token, configuracion.CrearParametrosValidacion());

        Assert.True(resultado.IsValid, resultado.Exception?.GetType().Name);
        var principal = new ClaimsPrincipal(resultado.ClaimsIdentity);
        Assert.True(LectorUsuarioActual.TryCrear(principal, out var actual));
        Assert.NotNull(actual);
        Assert.Equal(rol, actual.Rol);
        Assert.Equal(distribuidorId, actual.DistribuidorId);
        Assert.Equal(usuario.Nombre, principal.Identity?.Name);
        Assert.True(principal.IsInRole(rol.ToString()));
    }

    [Theory]
    [InlineData("expirado")]
    [InlineData("futuro")]
    [InlineData("sinExp")]
    [InlineData("emisor")]
    [InlineData("audiencia")]
    [InlineData("audienciaSlash")]
    [InlineData("sinAudiencia")]
    [InlineData("firma")]
    [InlineData("HS384")]
    public async Task Validar_RechazaTokensCriptograficamenteInvalidos(string caso)
    {
        var ahora = DateTimeOffset.UtcNow;
        var configuracion = CrearConfiguracion();
        // HS384 necesita una clave suficiente para firmar, aun cuando la validacion solo permite HS256.
        configuracion.Clave = new string('k', 64);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = caso == "emisor" ? "Otro.Emisor" : configuracion.Emisor,
            Audience = caso == "audiencia" ? "Otra.Audiencia"
                : caso == "audienciaSlash" ? configuracion.Audiencia + "/"
                : caso == "sinAudiencia" ? null : configuracion.Audiencia,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "Operador")
            }),
            IssuedAt = ahora.UtcDateTime.AddMinutes(-20),
            NotBefore = caso == "futuro" ? ahora.UtcDateTime.AddMinutes(10) : ahora.UtcDateTime.AddMinutes(-20),
            Expires = caso == "sinExp" ? null : caso == "expirado"
                ? ahora.UtcDateTime.AddMinutes(-10) : ahora.UtcDateTime.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuracion.Clave)),
                caso == "HS384" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256)
        };
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false, MapInboundClaims = false };
        string token = handler.CreateToken(descriptor);
        if (caso == "firma")
        {
            var partes = token.Split('.');
            byte[] firma = Base64UrlEncoder.DecodeBytes(partes[2]);
            firma[0] ^= 1;
            token = partes[0] + "." + partes[1] + "." + Base64UrlEncoder.Encode(firma);
        }

        var resultado = await handler.ValidateTokenAsync(token, configuracion.CrearParametrosValidacion());

        Assert.False(resultado.IsValid);
    }

    [Theory]
    [MemberData(nameof(UsuarioActualTests.CasosClaimsInvalidos), MemberType = typeof(UsuarioActualTests))]
    public async Task Validar_RechazaClaimsIncoherentesAunqueLaFirmaSeaValida(string caso)
    {
        var ahora = DateTimeOffset.UtcNow;
        var configuracion = CrearConfiguracion();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = configuracion.Emisor,
            Audience = configuracion.Audiencia,
            Subject = new ClaimsIdentity(UsuarioActualTests.CrearClaimsInvalidos(caso)),
            IssuedAt = ahora.UtcDateTime,
            NotBefore = ahora.UtcDateTime,
            Expires = ahora.UtcDateTime.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuracion.Clave)), SecurityAlgorithms.HmacSha256)
        };
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        string token = handler.CreateToken(descriptor);

        var resultado = await handler.ValidateTokenAsync(token, configuracion.CrearParametrosValidacion());

        // El handler rechaza sub como array antes de construir la identidad.
        if (caso == "subDuplicado")
        {
            Assert.False(resultado.IsValid);
            return;
        }
        Assert.True(resultado.IsValid, resultado.Exception?.GetType().Name);
        Assert.False(LectorUsuarioActual.TryCrear(new ClaimsPrincipal(resultado.ClaimsIdentity), out var actual));
        Assert.Null(actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("corta")]
    [InlineData("0123456789012345678901234567890")]
    [InlineData("ááááááááááááááá")]
    public void Validar_RechazaClaveAusenteOConMenosDe32BytesSinRevelarla(string? clave)
    {
        var configuracion = CrearConfiguracion();
        configuracion.Clave = clave!;

        var excepcion = Assert.Throws<InvalidOperationException>(() => configuracion.Validar());
        Assert.Equal("Configura Jwt__Clave con al menos 32 bytes UTF-8.", excepcion.Message);
        if (!string.IsNullOrWhiteSpace(clave))
        {
            Assert.DoesNotContain(clave, excepcion.Message);
        }
        Assert.Throws<InvalidOperationException>(() => configuracion.CrearParametrosValidacion());
        Assert.Throws<InvalidOperationException>(() => new GeneradorToken(configuracion));
    }

    [Theory]
    [InlineData("01234567890123456789012345678901", 32)]
    [InlineData("áááááááááááááááá", 32)]
    [InlineData("012345678901234567890123456789012", 33)]
    public void Validar_AceptaClaveDesde32BytesUtf8(string clave, int bytes)
    {
        var configuracion = CrearConfiguracion();
        configuracion.Clave = clave;

        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(clave));
        configuracion.Validar();
        Assert.Equal(Encoding.UTF8.GetBytes(clave),
            Assert.IsType<SymmetricSecurityKey>(configuracion.CrearParametrosValidacion().IssuerSigningKey).Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Validar_RechazaEmisorYAudienciaBlancos(string? valor)
    {
        var configuracion = CrearConfiguracion();
        configuracion.Emisor = valor!;
        Assert.Throws<InvalidOperationException>(() => configuracion.Validar());
        configuracion.Emisor = "Refidomsa.Api";
        configuracion.Audiencia = valor!;
        Assert.Throws<InvalidOperationException>(() => configuracion.Validar());
    }

    [Fact]
    public void CrearParametrosValidacion_ExigeFirmaHs256ExpiracionYDestinatarios()
    {
        var configuracion = CrearConfiguracion();
        var parametros = configuracion.CrearParametrosValidacion();

        Assert.True(parametros.RequireSignedTokens);
        Assert.True(parametros.ValidateIssuerSigningKey);
        Assert.True(parametros.RequireExpirationTime);
        Assert.True(parametros.ValidateLifetime);
        Assert.True(parametros.ValidateIssuer);
        Assert.True(parametros.ValidateAudience);
        Assert.True(parametros.RequireAudience);
        Assert.False(parametros.IgnoreTrailingSlashWhenValidatingAudience);
        Assert.Equal(configuracion.Emisor, parametros.ValidIssuer);
        Assert.Equal(configuracion.Audiencia, parametros.ValidAudience);
        Assert.Equal(SecurityAlgorithms.HmacSha256, Assert.Single(parametros.ValidAlgorithms));
        Assert.Equal(TimeSpan.FromSeconds(30), parametros.ClockSkew);
        Assert.Equal("name", parametros.NameClaimType);
        Assert.Equal("role", parametros.RoleClaimType);
    }
}
