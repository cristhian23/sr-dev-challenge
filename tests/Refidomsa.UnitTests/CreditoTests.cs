using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.DTOs.Credito;
using Refidomsa.Api.DTOs.Productos;
using Refidomsa.Api.Exceptions;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Rules;
using Refidomsa.Api.Security;
using Refidomsa.Api.Services;
using Xunit;

namespace Refidomsa.UnitTests;

public class CreditoTests
{
    [Theory]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 1000, 0)]
    [InlineData(1000, 1200, -200)]
    [InlineData(1000.25, 100.10, 900.15)]
    public void CalcularDisponible_ConservaSaldoExactoInclusoNegativo(decimal limite, decimal consumido,
        decimal esperado)
    {
        Assert.Equal(esperado, ReglasCredito.CalcularDisponible(limite, consumido));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void CalcularDisponible_RechazaImportesInvalidos(decimal limite, decimal consumido)
    {
        var error = Assert.Throws<ReglaNegocioException>(() => ReglasCredito.CalcularDisponible(limite, consumido));
        Assert.Equal("credito_invalido", error.Codigo);
    }

    [Fact]
    public async Task CreditoAjeno_NoConsultaBaseDeDatos()
    {
        // Una conexion no utilizable hace fallar cualquier consulta, sin abrir una DB de prueba.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("conexion-no-utilizable").Options);
        var servicio = new CreditoService(db);
        var usuario = new UsuarioActual(Rol.Distribuidor, Guid.NewGuid());

        Assert.Null(await servicio.ObtenerAsync(Guid.NewGuid(), usuario, CancellationToken.None));
    }

    [Fact]
    public void CreditoRespuesta_SerializaSoloContratoPublicoConDecimales()
    {
        var respuesta = new CreditoRespuesta
        {
            DistribuidorId = Guid.NewGuid(), LimiteCredito = 1000.25m,
            CreditoConsumido = 1200.50m, CreditoDisponible = -200.25m
        };
        using var documento = JsonDocument.Parse(JsonSerializer.Serialize(respuesta,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var raiz = documento.RootElement;

        Assert.Equal(new[] { "creditoConsumido", "creditoDisponible", "distribuidorId", "limiteCredito" },
            raiz.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        Assert.Equal(respuesta.DistribuidorId, raiz.GetProperty("distribuidorId").GetGuid());
        Assert.Equal(1000.25m, raiz.GetProperty("limiteCredito").GetDecimal());
        Assert.Equal(1200.50m, raiz.GetProperty("creditoConsumido").GetDecimal());
        Assert.Equal(-200.25m, raiz.GetProperty("creditoDisponible").GetDecimal());
    }

    [Fact]
    public void ProductoRespuesta_SerializaSoloCatalogoConPrecioPreciso()
    {
        var respuesta = new ProductoRespuesta { Id = Guid.NewGuid(), Nombre = "Producto", PrecioPorGalon = 290.123456m };
        using var documento = JsonDocument.Parse(JsonSerializer.Serialize(respuesta,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var raiz = documento.RootElement;

        Assert.Equal(new[] { "id", "nombre", "precioPorGalon" },
            raiz.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        Assert.Equal(respuesta.Id, raiz.GetProperty("id").GetGuid());
        Assert.Equal("Producto", raiz.GetProperty("nombre").GetString());
        Assert.Equal(290.123456m, raiz.GetProperty("precioPorGalon").GetDecimal());
    }
}
