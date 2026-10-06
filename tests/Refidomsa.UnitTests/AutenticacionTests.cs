using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Refidomsa.Api.DTOs.Autenticacion;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;
using Xunit;

namespace Refidomsa.UnitTests;

public class AutenticacionTests
{
    private static Usuario CrearUsuario()
    {
        return new Usuario(Guid.NewGuid(), "Usuario de prueba", " USUARIO.PRUEBA ", Rol.Operador, null);
    }

    [Theory]
    [InlineData("Password de prueba", PasswordVerificationResult.Success)]
    [InlineData("Password incorrecto", PasswordVerificationResult.Failed)]
    public void PasswordHasher_VerificaPasswordCorrectoEIncorrecto(string password,
        PasswordVerificationResult esperado)
    {
        var usuario = CrearUsuario();
        var hasher = new PasswordHasher<Usuario>();
        usuario.EstablecerPasswordHash(hasher.HashPassword(usuario, "Password de prueba"));

        var resultado = hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password);

        Assert.Equal(esperado, resultado);
        Assert.Equal("usuario.prueba", usuario.NombreUsuario);
    }

    [Fact]
    public void PasswordHasher_IdentificaHashQueNecesitaRehashSinModificarlo()
    {
        var usuario = CrearUsuario();
        var hasherAnterior = new PasswordHasher<Usuario>(Options.Create(
            new PasswordHasherOptions { IterationCount = 1 }));
        string hash = hasherAnterior.HashPassword(usuario, "Password de prueba");
        usuario.EstablecerPasswordHash(hash);

        var resultado = new PasswordHasher<Usuario>().VerifyHashedPassword(
            usuario, usuario.PasswordHash, "Password de prueba");

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, resultado);
        Assert.Equal(hash, usuario.PasswordHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" \t\r\n ")]
    public void LoginSolicitud_RechazaNombreUsuarioAusenteOVacio(string? nombreUsuario)
    {
        var solicitud = new LoginSolicitud { NombreUsuario = nombreUsuario!, Password = "Password valido" };
        var errores = new List<ValidationResult>();

        bool valido = Validator.TryValidateObject(solicitud, new ValidationContext(solicitud), errores, true);

        Assert.False(valido);
        Assert.Contains(nameof(LoginSolicitud.NombreUsuario), Assert.Single(errores).MemberNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" \t\r\n ")]
    public void LoginSolicitud_RechazaPasswordAusenteOVacio(string? password)
    {
        var solicitud = new LoginSolicitud { NombreUsuario = "usuario", Password = password! };
        var errores = new List<ValidationResult>();

        bool valido = Validator.TryValidateObject(solicitud, new ValidationContext(solicitud), errores, true);

        Assert.False(valido);
        Assert.Contains(nameof(LoginSolicitud.Password), Assert.Single(errores).MemberNames);
    }

    [Fact]
    public void LoginSolicitud_ValidaSinTransformarCredencialesNiRecortarPassword()
    {
        var solicitud = new LoginSolicitud
        {
            NombreUsuario = " USUARIO.PRUEBA ",
            Password = " Password con espacios "
        };
        var errores = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(solicitud, new ValidationContext(solicitud), errores, true));
        Assert.Empty(errores);
        Assert.Equal(" USUARIO.PRUEBA ", solicitud.NombreUsuario);
        Assert.Equal(" Password con espacios ", solicitud.Password);

        var usuario = CrearUsuario();
        var hasher = new PasswordHasher<Usuario>();
        usuario.EstablecerPasswordHash(hasher.HashPassword(usuario, solicitud.Password));
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, solicitud.Password));
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, solicitud.Password.Trim()));
    }

    [Fact]
    public void LoginSolicitud_ContieneSoloNombreUsuarioYPassword()
    {
        var propiedades = typeof(LoginSolicitud).GetProperties().Select(propiedad => propiedad.Name);

        Assert.Equal(new[] { "NombreUsuario", "Password" }, propiedades.OrderBy(nombre => nombre));
    }

    [Theory]
    [InlineData(Rol.Operador)]
    [InlineData(Rol.Distribuidor)]
    public void LoginRespuesta_SerializaSoloContratoPublicoSinPasswordNiHash(Rol rol)
    {
        Guid? distribuidorId = rol == Rol.Distribuidor ? Guid.NewGuid() : null;
        var usuario = new Usuario(Guid.NewGuid(), "Usuario", "usuario", rol, distribuidorId);
        usuario.EstablecerPasswordHash("hash-privado-que-no-debe-serializarse");
        var respuesta = new LoginRespuesta
        {
            Token = "token-de-prueba",
            ExpiraEnUtc = new DateTimeOffset(2026, 10, 6, 13, 0, 0, TimeSpan.Zero),
            Usuario = new UsuarioAutenticadoRespuesta
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                NombreUsuario = usuario.NombreUsuario,
                Rol = usuario.Rol.ToString(),
                DistribuidorId = usuario.DistribuidorId
            }
        };

        string json = JsonSerializer.Serialize(respuesta, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;
        Assert.Equal(new[] { "expiraEnUtc", "token", "usuario" },
            raiz.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        Assert.Equal(respuesta.Token, raiz.GetProperty("token").GetString());
        Assert.Equal(respuesta.ExpiraEnUtc, raiz.GetProperty("expiraEnUtc").GetDateTimeOffset());
        var datosUsuario = raiz.GetProperty("usuario");
        Assert.Equal(new[] { "distribuidorId", "id", "nombre", "nombreUsuario", "rol" },
            datosUsuario.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        Assert.Equal(usuario.Id, datosUsuario.GetProperty("id").GetGuid());
        Assert.Equal(usuario.Nombre, datosUsuario.GetProperty("nombre").GetString());
        Assert.Equal(usuario.NombreUsuario, datosUsuario.GetProperty("nombreUsuario").GetString());
        Assert.Equal(rol.ToString(), datosUsuario.GetProperty("rol").GetString());
        if (distribuidorId.HasValue)
        {
            Assert.Equal(distribuidorId.Value, datosUsuario.GetProperty("distribuidorId").GetGuid());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, datosUsuario.GetProperty("distribuidorId").ValueKind);
        }
        Assert.DoesNotContain("password", json.ToLowerInvariant());
        Assert.DoesNotContain("hash", json.ToLowerInvariant());
        Assert.DoesNotContain(usuario.PasswordHash, json);
    }
}
