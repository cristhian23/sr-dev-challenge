using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.DTOs.Pedidos;
using Refidomsa.Api.Models;
using Xunit;

namespace Refidomsa.UnitTests;

public class CambiarEstadoSolicitudTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("aprobado")]
    [InlineData(" Aprobado ")]
    [InlineData("Aprobado,Rechazado")]
    [InlineData("Inexistente")]
    public void Solicitud_RechazaEstadoAusenteONoLiteral(string? estado)
    {
        Assert.NotEmpty(Validar(new CambiarEstadoSolicitud { NuevoEstado = estado }));
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Aprobado")]
    [InlineData("Despachado")]
    [InlineData("Cancelado")]
    public void Solicitud_OtrosEstadosNoExigenMotivo(string estado)
    {
        Assert.Empty(Validar(new CambiarEstadoSolicitud { NuevoEstado = estado }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void Solicitud_RechazadoExigeMotivo(string? motivo)
    {
        Assert.NotEmpty(Validar(new CambiarEstadoSolicitud { NuevoEstado = "Rechazado", Motivo = motivo }));
    }

    [Theory]
    [InlineData("Rechazado")]
    [InlineData("Aprobado")]
    public void Solicitud_RespetaLongitudSqlDelMotivo(string estado)
    {
        Assert.Empty(Validar(new CambiarEstadoSolicitud { NuevoEstado = estado, Motivo = new string('x', 1000) }));
        Assert.NotEmpty(Validar(new CambiarEstadoSolicitud { NuevoEstado = estado, Motivo = new string('x', 1001) }));
    }

    [Theory]
    [InlineData("{\"nuevoEstado\":1}")]
    [InlineData("{\"nuevoEstado\":true}")]
    [InlineData("{\"nuevoEstado\":{}}")]
    public void Json_NoConvierteNumerosOBoleanosAEstado(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CambiarEstadoSolicitud>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Modelo_EstadoEsTokenAplicativoSobreColumnaExistente()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("conexion-no-utilizable").Options);
        var propiedad = db.Model.FindEntityType(typeof(Pedido))!.FindProperty(nameof(Pedido.Estado))!;
        Assert.True(propiedad.IsConcurrencyToken);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, propiedad.ValueGenerated);
        Assert.Equal("Estado", propiedad.GetColumnName());
        Assert.Equal(typeof(string), propiedad.GetTypeMapping().Converter!.ProviderClrType);
    }

    private static List<ValidationResult> Validar(CambiarEstadoSolicitud solicitud)
    {
        var errores = new List<ValidationResult>();
        Validator.TryValidateObject(solicitud, new ValidationContext(solicitud), errores, validateAllProperties: true);
        return errores;
    }
}
