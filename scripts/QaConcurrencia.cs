#:project ../src/Refidomsa.Api/Refidomsa.Api.csproj
#:property PublishAot=false

using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Refidomsa.Api.Data;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Security;

public class QaConcurrencia
{
    public static async Task Main()
    {
        var conexion = Environment.GetEnvironmentVariable("ConnectionStrings__Refidomsa")
            ?? throw new InvalidOperationException("Falta conexion QA.");
        var catalogo = new SqlConnectionStringBuilder(conexion).InitialCatalog;
        const string prefijo = "Refidomsa_QA_";
        if (!catalogo.StartsWith(prefijo, StringComparison.Ordinal)
            || !Guid.TryParseExact(catalogo.Substring(prefijo.Length), "N", out _))
        {
            throw new InvalidOperationException("Esta prueba solo admite un catalogo QA aislado.");
        }
        var id = Guid.Parse(Environment.GetEnvironmentVariable("QA_PEDIDO_ID")!);
        var opciones = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(conexion).Options;
        await using var primero = new AppDbContext(opciones);
        await using var segundo = new AppDbContext(opciones);
        var pedidoPrimero = await primero.Pedidos.Include(pedido => pedido.Lineas).SingleAsync(pedido => pedido.Id == id);
        var pedidoSegundo = await segundo.Pedidos.Include(pedido => pedido.Lineas).SingleAsync(pedido => pedido.Id == id);
        if (pedidoPrimero.Estado != EstadoPedido.Pendiente || pedidoSegundo.Estado != EstadoPedido.Pendiente)
        {
            throw new InvalidOperationException("El fixture debe estar Pendiente en ambos contextos.");
        }
        var operador = new UsuarioActual(Rol.Operador, null);
        pedidoPrimero.CambiarEstado(EstadoPedido.Aprobado, operador, DateTimeOffset.UtcNow);
        pedidoSegundo.CambiarEstado(EstadoPedido.Rechazado, operador, DateTimeOffset.UtcNow, "QA escritor obsoleto");
        await primero.SaveChangesAsync();
        bool conflicto = false;
        try
        {
            await segundo.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            conflicto = true;
        }
        if (!conflicto)
        {
            throw new InvalidOperationException("El segundo contexto sobrescribio el estado original.");
        }
        await using var lectura = new AppDbContext(opciones);
        var final = await lectura.Pedidos.AsNoTracking().SingleAsync(pedido => pedido.Id == id);
        if (final.Estado != EstadoPedido.Aprobado || final.MotivoRechazo != null
            || final.FechaCambioEstado != pedidoPrimero.FechaCambioEstado || final.Total != pedidoPrimero.Total)
        {
            throw new InvalidOperationException("El escritor obsoleto altero los datos del ganador.");
        }
        Console.WriteLine("PASS dos AppDbContext SQL: segundo SaveChanges lanza DbUpdateConcurrencyException, ganador intacto");
    }
}
