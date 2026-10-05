using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Refidomsa.Api.Data;
using Refidomsa.Api.Models;

namespace Refidomsa.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        bool inicializar = args.Contains("--initialize-db");
        bool verificar = args.Contains("--verify-db");
        var argumentosHost = args.Where(argumento => argumento != "--initialize-db" && argumento != "--verify-db").ToArray();
        var builder = WebApplication.CreateBuilder(argumentosHost);
        string? connectionString = builder.Configuration.GetConnectionString("Refidomsa");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Configura ConnectionStrings__Refidomsa o utiliza scripts/local.ps1.");
        }

        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        builder.Services.AddScoped<PasswordHasher<Usuario>>();
        builder.Services.AddScoped<DatosSemilla>();
        builder.Services.AddScoped<VerificadorPersistencia>();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks().AddCheck<SqlServerHealthCheck>("sqlserver");

        var app = builder.Build();
        if (inicializar || verificar)
        {
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException("Los comandos de semilla y diagnostico son solo para Development.");
            }
            string? password = builder.Configuration["DatabaseSeed:Password"];
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Define DatabaseSeed__Password para los usuarios de prueba.");
            }
            await using var scope = app.Services.CreateAsyncScope();
            if (inicializar)
            {
                await scope.ServiceProvider.GetRequiredService<DatosSemilla>().InicializarAsync(password);
                Console.WriteLine("Migraciones aplicadas y datos semilla disponibles.");
            }
            if (verificar)
            {
                await scope.ServiceProvider.GetRequiredService<VerificadorPersistencia>().VerificarAsync(password);
            }
            await app.DisposeAsync();
            return;
        }

        app.UseExceptionHandler();
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");
        await app.RunAsync();
    }
}
