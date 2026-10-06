using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Refidomsa.Api.Exceptions;
using Refidomsa.Api.Middlewares;
using Xunit;

namespace Refidomsa.UnitTests;

public class ErroresMiddlewareTests
{
    [Theory]
    [InlineData("producto_invalido", 400)]
    [InlineData("motivo_requerido", 400)]
    [InlineData("sin_permiso", 403)]
    [InlineData("credito_insuficiente", 409)]
    [InlineData("transicion_invalida", 409)]
    [InlineData("conflicto_concurrencia", 409)]
    public async Task InvokeAsync_TraduceReglasAProblemDetails(string codigo, int status)
    {
        using var servicios = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = servicios };
        context.Request.Path = "/api/prueba";
        context.Response.Body = new MemoryStream();
        var middleware = new ErroresMiddleware(_ => throw new ReglaNegocioException(codigo, "Detalle de prueba"));

        await middleware.InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var documento = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(status, documento.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(documento.RootElement.GetProperty("title").GetString()));
        Assert.Equal("Detalle de prueba", documento.RootElement.GetProperty("detail").GetString());
        Assert.Equal("/api/prueba", documento.RootElement.GetProperty("instance").GetString());
        Assert.Equal(codigo, documento.RootElement.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task InvokeAsync_NoOcultaFallosTecnicosComoErroresDeNegocio()
    {
        var context = new DefaultHttpContext();
        var errorOperacion = new InvalidOperationException("Fallo tecnico");
        var errorDb = new DbUpdateException("Fallo SQL");

        Assert.Same(errorOperacion, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ErroresMiddleware(_ => throw errorOperacion).InvokeAsync(context)));
        Assert.Same(errorDb, await Assert.ThrowsAsync<DbUpdateException>(() =>
            new ErroresMiddleware(_ => throw errorDb).InvokeAsync(context)));
    }
}
