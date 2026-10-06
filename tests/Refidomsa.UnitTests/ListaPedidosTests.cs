using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.DTOs.Pedidos;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Security;
using Refidomsa.Api.Services;
using Xunit;

namespace Refidomsa.UnitTests;

public class ListaPedidosTests
{
    [Fact]
    public void Solicitud_DefaultsValidos()
    {
        var solicitud = new ListaPedidosSolicitud();
        Assert.Equal(1, solicitud.Pagina);
        Assert.Equal(10, solicitud.TamanoPagina);
        Assert.Empty(Validar(solicitud));
        Assert.Null(solicitud.ObtenerDesde());
        Assert.Null(solicitud.ObtenerHasta());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(1, -1)]
    [InlineData(int.MaxValue, 10)]
    [InlineData(21474838, 100)]
    public void Solicitud_RechazaPaginacionInvalidaOSkipOverflow(int pagina, int tamano)
    {
        Assert.NotEmpty(Validar(new ListaPedidosSolicitud { Pagina = pagina, TamanoPagina = tamano }));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(21474837, 100)]
    public void Solicitud_AceptaLimitesSinOverflow(int pagina, int tamano)
    {
        Assert.Empty(Validar(new ListaPedidosSolicitud { Pagina = pagina, TamanoPagina = tamano }));
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Aprobado")]
    [InlineData("Despachado")]
    [InlineData("Rechazado")]
    [InlineData("Cancelado")]
    public void Solicitud_AceptaEstadosLiterales(string estado)
    {
        Assert.Empty(Validar(new ListaPedidosSolicitud { Estado = estado }));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("pendiente")]
    [InlineData("Inexistente")]
    [InlineData("Pendiente,Aprobado")]
    public void Solicitud_RechazaEstadoNoLiteral(string estado)
    {
        Assert.Contains(Validar(new ListaPedidosSolicitud { Estado = estado }),
            error => error.MemberNames.Contains("Estado"));
    }

    [Theory]
    [InlineData("2026-09-01")]
    [InlineData("2026-09-01T12:00:00")]
    [InlineData("2026-02-30T12:00:00Z")]
    [InlineData("2026-09-01T12:00:00+15:00")]
    [InlineData("no")]
    public void Solicitud_RechazaFechaSinOffsetOInvalida(string fecha)
    {
        var errores = Validar(new ListaPedidosSolicitud { Desde = fecha, Hasta = fecha });
        Assert.Contains(errores, error => error.MemberNames.Contains("Desde"));
        Assert.Contains(errores, error => error.MemberNames.Contains("Hasta"));
    }

    [Theory]
    [InlineData("2026-09-01T04:00:00Z")]
    [InlineData("2026-09-01T00:00:00-04:00")]
    [InlineData("2026-09-01T06:00:00+02:00")]
    public void Solicitud_ConservaInstanteDeOffsets(string fecha)
    {
        var solicitud = new ListaPedidosSolicitud { Desde = fecha, Hasta = "2026-09-02T04:00:00Z" };
        Assert.Empty(Validar(solicitud));
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T04:00:00Z"), solicitud.ObtenerDesde());
    }

    [Theory]
    [InlineData("2026-09-01T04:00:00Z")]
    [InlineData("2026-09-01T00:00:00-04:00")]
    [InlineData("2026-09-02T04:00:00Z")]
    public void Solicitud_RechazaRangoIgualOInvertido(string desde)
    {
        Assert.NotEmpty(Validar(new ListaPedidosSolicitud { Desde = desde, Hasta = "2026-09-01T04:00:00Z" }));
    }

    [Fact]
    public void Solicitud_RechazaDistribuidorVacio()
    {
        Assert.Contains(Validar(new ListaPedidosSolicitud { DistribuidorId = Guid.Empty }),
            error => error.MemberNames.Contains("DistribuidorId"));
    }

    [Fact]
    public async Task FiltroAjeno_NoCuentaNiConsultaSQL()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("conexion-no-utilizable").Options);
        var servicio = new PedidosService(db);
        var resultado = await servicio.ListarAsync(new ListaPedidosSolicitud { DistribuidorId = Guid.NewGuid() },
            new UsuarioActual(Rol.Distribuidor, Guid.NewGuid()), CancellationToken.None);
        Assert.Null(resultado);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SkipOverflow_SeDetectaAntesDeConsultarSQL()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("conexion-no-utilizable").Options);
        var servicio = new PedidosService(db);
        await Assert.ThrowsAsync<OverflowException>(() => servicio.ListarAsync(
            new ListaPedidosSolicitud { Pagina = int.MaxValue },
            new UsuarioActual(Rol.Operador, null), CancellationToken.None));
    }

    [Fact]
    public void Lista_SerializaSoloResumenSinLineasNiMotivoConEstadoLiteral()
    {
        var lista = new ListaPedidosRespuesta
        {
            Pagina = 1, TamanoPagina = 10, TotalRegistros = 1,
            Items = new List<PedidoResumenRespuesta>
            {
                new PedidoResumenRespuesta { Id = Guid.NewGuid(), DistribuidorId = Guid.NewGuid(),
                    NombreDistribuidor = "Norte", Estado = EstadoPedido.Aprobado, Total = 145050.04m }
            }
        };
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opciones.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        using var documento = JsonDocument.Parse(JsonSerializer.Serialize(lista, opciones));
        Assert.Equal(new[] { "items", "pagina", "tamanoPagina", "totalRegistros" },
            documento.RootElement.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        var resumen = documento.RootElement.GetProperty("items")[0];
        Assert.Equal(new[] { "distribuidorId", "estado", "fechaCambioEstado", "fechaCreacion", "fechaEntrega",
            "id", "nombreDistribuidor", "total" },
            resumen.EnumerateObject().Select(propiedad => propiedad.Name).OrderBy(nombre => nombre));
        Assert.Equal("Aprobado", resumen.GetProperty("estado").GetString());
        Assert.Equal(145050.04m, resumen.GetProperty("total").GetDecimal());
    }

    private static List<ValidationResult> Validar(ListaPedidosSolicitud solicitud)
    {
        var errores = new List<ValidationResult>();
        Validator.TryValidateObject(solicitud, new ValidationContext(solicitud), errores, validateAllProperties: true);
        return errores;
    }
}
