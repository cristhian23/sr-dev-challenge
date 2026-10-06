using Microsoft.AspNetCore.Mvc;
using Refidomsa.Api.Exceptions;

namespace Refidomsa.Api.Middlewares;

public class ErroresMiddleware
{
    private readonly RequestDelegate _next;

    public ErroresMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ReglaNegocioException exception) when (!context.Response.HasStarted)
        {
            int status = exception.Codigo switch
            {
                "sin_permiso" => StatusCodes.Status403Forbidden,
                "credito_insuficiente" or "transicion_invalida" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            var problema = new ProblemDetails
            {
                Status = status,
                Title = status == 403 ? "Acceso denegado" : status == 409 ? "Conflicto" : "Solicitud invalida",
                Detail = exception.Message,
                Instance = context.Request.Path
            };
            problema.Extensions["codigo"] = exception.Codigo;
            context.Response.StatusCode = status;
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
                new ProblemDetailsContext { HttpContext = context, ProblemDetails = problema });
        }
    }
}
