using Refidomsa.Api.Exceptions;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Rules;
using Refidomsa.Api.Security;
using Xunit;

namespace Refidomsa.UnitTests;

public class PedidoTests
{
    private static readonly Guid DistribuidorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly UsuarioActual Distribuidor = new UsuarioActual(Rol.Distribuidor, DistribuidorId);
    private static readonly UsuarioActual Operador = new UsuarioActual(Rol.Operador, null);
    private static readonly DateTimeOffset Ahora = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static Producto CrearProducto(decimal precio = 100)
    {
        return new Producto(Guid.NewGuid(), "Gasolina Premium", precio);
    }

    private static Pedido CrearPedido(decimal galones = 500, UsuarioActual? usuarioActual = null)
    {
        var lineas = new[] { new SolicitudLinea(CrearProducto(), galones) };
        return Pedido.Crear(usuarioActual ?? Distribuidor, Ahora.AddDays(1), Ahora, lineas, 2_000_000);
    }

    private static void ComprobarReglaRechazada(string codigo, Action accion)
    {
        var excepcion = Assert.Throws<ReglaNegocioException>(accion);
        Assert.Equal(codigo, excepcion.Codigo);
    }

    [Fact]
    public void Crear_FijaEstadoPreciosYFechas()
    {
        var producto = CrearProducto();
        var entradas = new List<SolicitudLinea> { new SolicitudLinea(producto, 500) };
        var pedido = Pedido.Crear(Distribuidor, Ahora.AddDays(1), Ahora, entradas, 50_000);
        producto = new Producto(producto.Id, producto.Nombre, 200);
        entradas.Clear();
        Assert.Equal(EstadoPedido.Pendiente, pedido.Estado);
        Assert.Equal(DistribuidorId, pedido.DistribuidorId);
        Assert.NotEqual(Guid.Empty, pedido.Id);
        Assert.Equal(Ahora, pedido.FechaCreacion);
        Assert.Equal(Ahora, pedido.FechaCambioEstado);
        Assert.Equal(Ahora.AddDays(1), pedido.FechaEntrega);
        Assert.Equal(100m, pedido.Lineas[0].PrecioPorGalon);
        Assert.Equal(50_000m, pedido.Total);
        Assert.Single(pedido.Lineas);
        Assert.Throws<NotSupportedException>(() => ((IList<LineaPedido>)pedido.Lineas).Clear());
        Assert.Equal(200m, producto.PrecioPorGalon);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(9000)]
    public void Crear_AceptaLimitesDeGalones(int galones)
    {
        var pedido = CrearPedido(galones);
        Assert.Equal(galones, pedido.Lineas[0].Galones);
    }

    [Theory]
    [InlineData(499, "minimo_galones")]
    [InlineData(0, "minimo_galones")]
    [InlineData(-1, "minimo_galones")]
    [InlineData(9001, "capacidad_camion")]
    public void Crear_RechazaGalonesFueraDeLimites(int galones, string codigo)
    {
        ComprobarReglaRechazada(codigo, () => CrearPedido(galones));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Crear_RechazaCantidadDeLineas(int cantidad)
    {
        var lineas = Enumerable.Range(0, cantidad)
            .Select(_ => new SolicitudLinea(CrearProducto(), 500)).ToArray();
        ComprobarReglaRechazada("cantidad_lineas", () =>
            Pedido.Crear(Distribuidor, Ahora.AddDays(1), Ahora, lineas, 2_000_000));
    }

    [Fact]
    public void Crear_AceptaCuatroProductosYCapacidadExacta()
    {
        var pedido = Pedido.Crear(Distribuidor, Ahora.AddDays(1), Ahora,
            Enumerable.Range(0, 4).Select(_ => new SolicitudLinea(CrearProducto(), 2250)), 900_000);
        Assert.Equal(4, pedido.Lineas.Count);
        Assert.Equal(900_000m, pedido.Total);
    }

    [Fact]
    public void Crear_RechazaSumaMayorQueCapacidad()
    {
        var lineas = new[]
        {
            new SolicitudLinea(CrearProducto(), 4500),
            new SolicitudLinea(CrearProducto(), 4501)
        };
        ComprobarReglaRechazada("capacidad_camion", () =>
            Pedido.Crear(Distribuidor, Ahora.AddDays(1), Ahora, lineas, 2_000_000));
    }

    [Fact]
    public void Crear_RechazaProductoRepetido()
    {
        var producto = CrearProducto();
        var lineas = new[]
        {
            new SolicitudLinea(producto, 500),
            new SolicitudLinea(producto, 500)
        };
        ComprobarReglaRechazada("producto_repetido", () =>
            Pedido.Crear(Distribuidor, Ahora.AddDays(1), Ahora, lineas, 2_000_000));
    }

    [Fact]
    public void Crear_RechazaMenosDe24Horas()
    {
        var entrega = Ahora.AddHours(24).AddTicks(-1);
        var lineas = new[] { new SolicitudLinea(CrearProducto(), 500) };
        ComprobarReglaRechazada("anticipacion_entrega", () =>
            Pedido.Crear(Distribuidor, entrega, Ahora, lineas, 50_000));
    }

    [Fact]
    public void Crear_AceptaExactamente24Horas()
    {
        var pedido = CrearPedido();
        Assert.Equal(TimeSpan.FromHours(24), pedido.FechaEntrega - pedido.FechaCreacion);
    }

    [Fact]
    public void Crear_DomingoSeEvaluaEnHoraDominicana()
    {
        var lunesUtcDomingoLocal = new DateTimeOffset(2026, 10, 12, 2, 0, 0, TimeSpan.Zero);
        var lineas = new[] { new SolicitudLinea(CrearProducto(), 500) };
        ComprobarReglaRechazada("entrega_domingo", () =>
            Pedido.Crear(Distribuidor, lunesUtcDomingoLocal, Ahora, lineas, 50_000));
        var domingoUtcSabadoLocal = new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.Zero);
        var pedido = Pedido.Crear(Distribuidor, domingoUtcSabadoLocal, Ahora, lineas, 50_000);
        Assert.NotNull(pedido);
    }

    [Fact]
    public void Crear_RechazaCreditoInsuficiente()
    {
        var lineas = new[] { new SolicitudLinea(CrearProducto(), 500) };
        ComprobarReglaRechazada("credito_insuficiente", () =>
            Pedido.Crear(Distribuidor, Ahora.AddDays(1), Ahora, lineas, 49_999.99m));
    }

    [Fact]
    public void Crear_RedondeaSubtotalADosDecimales()
    {
        var lineas = new[] { new SolicitudLinea(CrearProducto(1.23457m), 500) };
        var pedido = Pedido.Crear(Distribuidor, Ahora.AddDays(1), Ahora, lineas, 1000);
        Assert.Equal(617.29m, pedido.Total);
    }

    [Fact]
    public void Crear_OperadorNoPuedeCrear()
    {
        ComprobarReglaRechazada("sin_permiso", () => CrearPedido(usuarioActual: Operador));
    }

    [Fact]
    public void Crear_DistribuidorDebeEstarAsociado()
    {
        var usuarioSinDistribuidor = new UsuarioActual(Rol.Distribuidor, null);
        ComprobarReglaRechazada("sin_permiso", () => CrearPedido(usuarioActual: usuarioSinDistribuidor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Crear_RechazaPrecioNoPositivo(int precio)
    {
        var lineas = new[] { new SolicitudLinea(CrearProducto(precio), 500) };
        ComprobarReglaRechazada("producto_invalido", () =>
            Pedido.Crear(Distribuidor, Ahora.AddDays(1), Ahora, lineas, 50_000));
    }

    public static IEnumerable<object[]> Transiciones
    {
        get
        {
            foreach (var origen in Enum.GetValues<EstadoPedido>())
            {
                foreach (var destino in Enum.GetValues<EstadoPedido>())
                {
                    yield return new object[] { origen, destino };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Transiciones))]
    public void CambiarEstado_RespetaTodaLaMatriz(EstadoPedido origen, EstadoPedido destino)
    {
        var pedido = CrearPedido();
        if (origen == EstadoPedido.Despachado)
        {
            pedido.CambiarEstado(EstadoPedido.Aprobado, Operador, Ahora.AddHours(1));
            pedido.CambiarEstado(origen, Operador, Ahora.AddHours(2));
        }
        else if (origen != EstadoPedido.Pendiente)
        {
            var usuarioInicial = origen == EstadoPedido.Cancelado ? Distribuidor : Operador;
            pedido.CambiarEstado(origen, usuarioInicial, Ahora.AddHours(1), "Motivo");
        }

        var transicionesPermitidas = new[]
        {
            (EstadoPedido.Pendiente, EstadoPedido.Aprobado),
            (EstadoPedido.Pendiente, EstadoPedido.Rechazado),
            (EstadoPedido.Pendiente, EstadoPedido.Cancelado),
            (EstadoPedido.Aprobado, EstadoPedido.Despachado)
        };
        bool permitido = transicionesPermitidas.Contains((origen, destino));
        var usuarioActual = destino == EstadoPedido.Cancelado ? Distribuidor : Operador;
        if (permitido)
        {
            pedido.CambiarEstado(destino, usuarioActual, Ahora.AddHours(3), "  Motivo  ");
            Assert.Equal(destino, pedido.Estado);
            Assert.Equal(Ahora.AddHours(3), pedido.FechaCambioEstado);
            Assert.Equal(destino == EstadoPedido.Rechazado ? "Motivo" : null, pedido.MotivoRechazo);
        }
        else
        {
            var fechaAnterior = pedido.FechaCambioEstado;
            ComprobarReglaRechazada("transicion_invalida", () => pedido.CambiarEstado(destino, usuarioActual, Ahora.AddHours(3), "Motivo"));
            Assert.Equal(origen, pedido.Estado);
            Assert.Equal(fechaAnterior, pedido.FechaCambioEstado);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rechazar_ExigeMotivoSinMutarPedido(string? motivo)
    {
        var pedido = CrearPedido();
        ComprobarReglaRechazada("motivo_requerido", () => pedido.CambiarEstado(EstadoPedido.Rechazado, Operador, Ahora.AddHours(1), motivo));
        Assert.Equal(EstadoPedido.Pendiente, pedido.Estado);
        Assert.Equal(Ahora, pedido.FechaCambioEstado);
    }

    [Fact]
    public void Cancelar_ExigePropietario()
    {
        var pedido = CrearPedido();
        var otroDistribuidor = new UsuarioActual(Rol.Distribuidor, Guid.NewGuid());
        ComprobarReglaRechazada("sin_permiso", () => pedido.CambiarEstado(EstadoPedido.Cancelado, otroDistribuidor, Ahora));
        ComprobarReglaRechazada("sin_permiso", () => pedido.CambiarEstado(EstadoPedido.Cancelado, Operador, Ahora));
    }

    [Theory]
    [InlineData(EstadoPedido.Aprobado)]
    [InlineData(EstadoPedido.Rechazado)]
    [InlineData(EstadoPedido.Despachado)]
    public void Distribuidor_NoPuedeRealizarAccionesDeOperador(EstadoPedido destino)
    {
        var pedido = CrearPedido();
        ComprobarReglaRechazada("sin_permiso", () => pedido.CambiarEstado(destino, Distribuidor, Ahora, "Motivo"));
    }

    [Fact]
    public void CambiarEstado_NoPermiteRetrocederFecha()
    {
        var pedido = CrearPedido();
        ComprobarReglaRechazada("fecha_estado_invalida", () =>
            pedido.CambiarEstado(EstadoPedido.Aprobado, Operador, Ahora.AddTicks(-1)));
    }

    [Theory]
    [InlineData(EstadoPedido.Pendiente, 50_000)]
    [InlineData(EstadoPedido.Aprobado, 50_000)]
    [InlineData(EstadoPedido.Despachado, 100_000)]
    [InlineData(EstadoPedido.Rechazado, 100_000)]
    [InlineData(EstadoPedido.Cancelado, 100_000)]
    public void Credito_ConsumeSoloPendientesYAprobadosDelDistribuidor(EstadoPedido estado, int esperado)
    {
        var pedido = CrearPedido();
        if (estado == EstadoPedido.Despachado)
        {
            pedido.CambiarEstado(EstadoPedido.Aprobado, Operador, Ahora);
        }
        if (estado != EstadoPedido.Pendiente)
        {
            var usuarioActual = estado == EstadoPedido.Cancelado ? Distribuidor : Operador;
            pedido.CambiarEstado(estado, usuarioActual, Ahora, "Motivo");
        }
        var otroDistribuidor = new UsuarioActual(Rol.Distribuidor, Guid.NewGuid());
        var ajeno = CrearPedido(usuarioActual: otroDistribuidor);
        var pedidos = new[] { pedido, ajeno };
        decimal disponible = ReglasCredito.CalcularDisponible(DistribuidorId, 100_000, pedidos);
        Assert.Equal(esperado, disponible);
    }
}
