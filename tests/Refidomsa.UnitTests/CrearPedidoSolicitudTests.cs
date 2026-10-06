using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.DTOs.Pedidos;
using Refidomsa.Api.Exceptions;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Security;
using Refidomsa.Api.Services;
using Xunit;

namespace Refidomsa.UnitTests;

public class CrearPedidoSolicitudTests
{
    [Theory]
    [InlineData("500")]
    [InlineData("500.123456")]
    [InlineData("500.1234560")]
    [InlineData("9000")]
    public void Linea_AceptaPrecisionCompatibleSinTruncar(string galones)
    {
        var linea = new CrearLineaPedidoSolicitud
        {
            ProductoId = Guid.NewGuid(),
            Galones = decimal.Parse(galones, System.Globalization.CultureInfo.InvariantCulture)
        };
        Assert.Empty(Validar(linea));
    }

    [Theory]
    [InlineData("500.1234567")]
    [InlineData("1000000000000")]
    [InlineData("-1000000000000")]
    [InlineData("79228162514264337593543950335")]
    public void Linea_RechazaEscalaORangoFueraDeSQL(string galones)
    {
        var linea = new CrearLineaPedidoSolicitud
        {
            ProductoId = Guid.NewGuid(),
            Galones = decimal.Parse(galones, System.Globalization.CultureInfo.InvariantCulture)
        };
        Assert.Contains(Validar(linea), error => error.MemberNames.Contains(nameof(linea.Galones)));
    }

    [Fact]
    public void Linea_RequiereProductoYGalones()
    {
        Assert.Equal(2, Validar(new CrearLineaPedidoSolicitud()).Count);
        Assert.Contains(Validar(new CrearLineaPedidoSolicitud { ProductoId = Guid.Empty, Galones = 500 }),
            error => error.MemberNames.Contains("ProductoId"));
    }

    [Theory]
    [InlineData("500.00000000000000000000000000001")]
    [InlineData("500.1234567")]
    [InlineData("5000000001e-7")]
    [InlineData("1e100")]
    [InlineData("1e-100")]
    public void GalonesJSON_RechazaPrecisionAntesDeConversionDecimal(string numero)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CrearLineaPedidoSolicitud>(
            "{\"galones\":" + numero + "}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("500.123456000000000000000000000000", "500.123456")]
    [InlineData("500123456e-6", "500.123456")]
    [InlineData("5e2", "500")]
    [InlineData("0.000000000000000000000000000000", "0")]
    public void GalonesJSON_ConservaNumeroExactoConExponentesOCeros(string numero, string esperado)
    {
        var linea = JsonSerializer.Deserialize<CrearLineaPedidoSolicitud>("{\"galones\":" + numero + "}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), linea.Galones);
    }

    [Fact]
    public void Solicitud_RequiereFechaYLineasYRechazaLineaNula()
    {
        Assert.Equal(2, Validar(new CrearPedidoSolicitud()).Count);
        var solicitud = new CrearPedidoSolicitud
        {
            FechaEntrega = DateTimeOffset.UtcNow.AddDays(2),
            Lineas = new List<CrearLineaPedidoSolicitud?> { null }
        };
        Assert.Contains(Validar(solicitud), error => error.MemberNames.Contains("Lineas"));
    }

    [Fact]
    public void Formato_NoDuplicaReglasDeCantidadMinimaNiCantidadDeLineas()
    {
        Assert.Empty(Validar(new CrearLineaPedidoSolicitud { ProductoId = Guid.NewGuid(), Galones = 499 }));
        Assert.Empty(Validar(new CrearPedidoSolicitud
        {
            FechaEntrega = DateTimeOffset.UtcNow,
            Lineas = new List<CrearLineaPedidoSolicitud?>()
        }));
    }

    [Fact]
    public void Solicitud_IgnoraCamposQueElClienteNoPuedeDeterminar()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var solicitud = JsonSerializer.Deserialize<CrearPedidoSolicitud>("""
            {"fechaEntrega":"2026-10-08T12:00:00-04:00","distribuidorId":"22222222-2222-2222-2222-222222222222",
             "total":1,"creditoDisponible":999999,"rol":"Operador","fechaCreacion":"2000-01-01T00:00:00Z",
             "lineas":[{"productoId":"aaaaaaaa-0000-0000-0000-000000000001","galones":500,"precioPorGalon":0.01,"subtotal":1}]}
            """, opciones)!;
        using var documento = JsonDocument.Parse(JsonSerializer.Serialize(solicitud, opciones));
        Assert.Equal(new[] { "fechaEntrega", "lineas" },
            documento.RootElement.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        Assert.Equal(new[] { "galones", "productoId" }, documento.RootElement.GetProperty("lineas")[0]
            .EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
    }

    [Fact]
    public async Task Operador_NoAbreTransaccionNiConsultaSQL()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("conexion-no-utilizable").Options);
        var servicio = new PedidosService(db);
        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => servicio.CrearAsync(
            new CrearPedidoSolicitud(), new UsuarioActual(Rol.Operador, null), CancellationToken.None));
        Assert.Equal("sin_permiso", error.Codigo);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Detalle_SerializaSoloContratoPublicoConEstadoLiteralYDecimales()
    {
        var respuesta = new PedidoDetalleRespuesta
        {
            Id = Guid.NewGuid(), DistribuidorId = Guid.NewGuid(), NombreDistribuidor = "Norte",
            FechaEntrega = DateTimeOffset.UtcNow.AddDays(2), FechaCreacion = DateTimeOffset.UtcNow,
            FechaCambioEstado = DateTimeOffset.UtcNow, Estado = EstadoPedido.Pendiente, Total = 145050.04m,
            Lineas = new List<LineaPedidoRespuesta>
            {
                new LineaPedidoRespuesta { ProductoId = Guid.NewGuid(), NombreProducto = "Premium",
                    Galones = 500.000123m, PrecioPorGalon = 290.1m, Subtotal = 145050.04m }
            }
        };
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opciones.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        using var documento = JsonDocument.Parse(JsonSerializer.Serialize(respuesta, opciones));
        var raiz = documento.RootElement;
        Assert.Equal(new[] { "distribuidorId", "estado", "fechaCambioEstado", "fechaCreacion", "fechaEntrega",
            "id", "lineas", "motivoRechazo", "nombreDistribuidor", "total" },
            raiz.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        Assert.Equal("Pendiente", raiz.GetProperty("estado").GetString());
        Assert.Equal(145050.04m, raiz.GetProperty("total").GetDecimal());
        var linea = raiz.GetProperty("lineas")[0];
        Assert.Equal(new[] { "galones", "nombreProducto", "precioPorGalon", "productoId", "subtotal" },
            linea.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        Assert.Equal(500.000123m, linea.GetProperty("galones").GetDecimal());
    }

    private static List<ValidationResult> Validar(object entrada)
    {
        var errores = new List<ValidationResult>();
        Validator.TryValidateObject(entrada, new ValidationContext(entrada), errores, validateAllProperties: true);
        return errores;
    }
}
