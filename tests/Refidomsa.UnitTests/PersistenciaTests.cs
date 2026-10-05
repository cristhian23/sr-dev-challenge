using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Security;
using Xunit;

namespace Refidomsa.UnitTests;

public class PersistenciaTests
{
    private static AppDbContext CrearContextoSinConexion()
    {
        // Se inspecciona el modelo o se rechaza un valor ANTES de acceder a SQL Server.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=NoUsar;Integrated Security=True")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Modelo_MapeaLineasPrivadasYPrecisionExplicita()
    {
        using var db = CrearContextoSinConexion();
        var modeloPedido = db.Model.FindEntityType(typeof(Pedido));
        Assert.NotNull(modeloPedido);
        var navegacion = modeloPedido.FindNavigation(nameof(Pedido.Lineas));
        Assert.NotNull(navegacion);
        Assert.Equal("_lineas", navegacion.FieldInfo?.Name);

        var modeloLinea = navegacion.TargetEntityType;
        Assert.True(modeloLinea.IsOwned());
        var clave = modeloLinea.FindPrimaryKey();
        Assert.NotNull(clave);
        Assert.Equal(new[] { "PedidoId", "ProductoId" }, clave.Properties.Select(propiedad => propiedad.Name));
        var galones = modeloLinea.FindProperty(nameof(LineaPedido.Galones));
        Assert.NotNull(galones);
        Assert.Equal(18, galones.GetPrecision());
        Assert.Equal(6, galones.GetScale());
    }

    [Theory]
    [InlineData("290.1234567")]
    [InlineData("1000000000000")]
    public void Guardar_RechazaPrecioQueSqlServerAlteraria(string precioTexto)
    {
        using var db = CrearContextoSinConexion();
        decimal precio = decimal.Parse(precioTexto, System.Globalization.CultureInfo.InvariantCulture);
        db.Productos.Add(new Producto(Guid.NewGuid(), "Producto de prueba", precio));
        var excepcion = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("PrecioPorGalon", excepcion.Message);
        Assert.Contains("decimal(18,6)", excepcion.Message);
    }

    [Fact]
    public async Task GuardarAsync_RechazaGalonesQueSqlServerRedondearia()
    {
        await using var db = CrearContextoSinConexion();
        var usuarioActual = new UsuarioActual(Rol.Distribuidor, Guid.NewGuid());
        var producto = new Producto(Guid.NewGuid(), "Gasolina Premium", 290.10m);
        var creacion = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var lineas = new[] { new SolicitudLinea(producto, 500.1234567m) };
        var pedido = Pedido.Crear(usuarioActual, creacion.AddDays(1), creacion, lineas, 3_000_000);
        db.Pedidos.Add(pedido);

        var excepcion = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("Galones", excepcion.Message);
    }

    [Theory]
    [InlineData(Rol.Distribuidor, false)]
    [InlineData(Rol.Operador, true)]
    public void Usuario_RechazaAsociacionDeDistribuidorIncompatibleConRol(Rol rol, bool tieneDistribuidor)
    {
        Guid? distribuidorId = tieneDistribuidor ? Guid.NewGuid() : null;
        Assert.Throws<ArgumentException>(() =>
            new Usuario(Guid.NewGuid(), "Usuario", "usuario", rol, distribuidorId));
    }

    [Fact]
    public void Distribuidor_RechazaLimiteDeCreditoNegativo()
    {
        Assert.Throws<ArgumentException>(() =>
            new Distribuidor(Guid.NewGuid(), "Distribuidor", "101000099", -1));
    }
}
