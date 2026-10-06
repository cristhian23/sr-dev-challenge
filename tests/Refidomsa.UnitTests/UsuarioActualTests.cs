using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Security;
using Xunit;

namespace Refidomsa.UnitTests;

public class UsuarioActualTests
{
    public static IEnumerable<object[]> CasosClaimsInvalidos()
    {
        string[] casos =
        {
            "subAusente", "subVacio", "subMalformado", "subGuidVacio", "subDuplicado",
            "roleAusente", "roleNumerico", "roleDesconocido", "roleMinusculas", "roleDuplicado",
            "distribuidorAusente", "distribuidorVacio", "distribuidorMalformado",
            "distribuidorGuidVacio", "distribuidorDuplicado", "operadorConDistribuidor"
        };
        return casos.Select(caso => new object[] { caso });
    }

    public static List<Claim> CrearClaimsInvalidos(string caso)
    {
        var claims = new List<Claim>
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role", "Distribuidor"),
            new Claim("distribuidorId", Guid.NewGuid().ToString())
        };
        string tipo = caso.StartsWith("sub") ? "sub"
            : caso.StartsWith("role") ? "role" : "distribuidorId";
        var original = claims.Single(claim => claim.Type == tipo);
        if (caso.EndsWith("Duplicado"))
        {
            claims.Add(new Claim(tipo, original.Value));
            return claims;
        }
        claims.Remove(original);
        string? valor = caso switch
        {
            "subAusente" or "roleAusente" or "distribuidorAusente" => null,
            "subVacio" or "distribuidorVacio" => "",
            "subGuidVacio" or "distribuidorGuidVacio" => Guid.Empty.ToString(),
            "roleNumerico" => "1",
            "roleDesconocido" => "Administrador",
            "roleMinusculas" => "distribuidor",
            "operadorConDistribuidor" => original.Value,
            _ => "no-es-guid"
        };
        if (valor != null)
        {
            claims.Add(new Claim(tipo, valor));
        }
        if (caso == "operadorConDistribuidor")
        {
            claims.RemoveAll(claim => claim.Type == "role");
            claims.Add(new Claim("role", "Operador"));
        }
        return claims;
    }

    [Theory]
    [InlineData(Rol.Operador)]
    [InlineData(Rol.Distribuidor)]
    public void TryCrearYObtener_ConservanRolYDistribuidorDesdeHttpContext(Rol rol)
    {
        Guid distribuidorId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role", rol.ToString())
        };
        if (rol == Rol.Distribuidor)
        {
            claims.Add(new Claim("distribuidorId", distribuidorId.ToString()));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", "name", "role"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };

        Assert.True(LectorUsuarioActual.TryCrear(principal, out var usuario));
        Assert.NotNull(usuario);
        Assert.Equal(rol, usuario.Rol);
        Assert.Equal(rol == Rol.Distribuidor ? distribuidorId : (Guid?)null, usuario.DistribuidorId);
        var obtenido = new LectorUsuarioActual(accessor).Obtener();
        Assert.Equal(usuario.Rol, obtenido.Rol);
        Assert.Equal(usuario.DistribuidorId, obtenido.DistribuidorId);
    }

    [Theory]
    [MemberData(nameof(CasosClaimsInvalidos))]
    public void TryCrearYObtener_RechazanClaimsInvalidos(string caso)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(CrearClaimsInvalidos(caso), "Bearer"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };

        Assert.False(LectorUsuarioActual.TryCrear(principal, out var usuario));
        Assert.Null(usuario);
        Assert.Throws<UnauthorizedAccessException>(() => new LectorUsuarioActual(accessor).Obtener());
    }

    [Fact]
    public void TryCrearYObtener_RechazanIdentidadAnonimaAunqueTengaClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "Operador")
        }));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };

        Assert.False(LectorUsuarioActual.TryCrear(principal, out var usuario));
        Assert.Null(usuario);
        Assert.Throws<UnauthorizedAccessException>(() => new LectorUsuarioActual(accessor).Obtener());
    }

    [Fact]
    public void Obtener_RechazaContextoAusente()
    {
        Assert.Throws<UnauthorizedAccessException>(() => new LectorUsuarioActual(new HttpContextAccessor()).Obtener());
    }

    [Fact]
    public void Obtener_RechazaUsuarioAusenteYNoLeeClaimsDesdeLaSolicitud()
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers["role"] = "Operador";
        contexto.Request.QueryString = new QueryString("?sub=" + Guid.NewGuid() + "&role=Operador");
        var lector = new LectorUsuarioActual(new HttpContextAccessor { HttpContext = contexto });

        Assert.Throws<UnauthorizedAccessException>(() => lector.Obtener());
    }
}
