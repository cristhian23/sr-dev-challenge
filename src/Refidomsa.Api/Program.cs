namespace Refidomsa.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapHealthChecks("/health");
        app.Run();
    }
}
