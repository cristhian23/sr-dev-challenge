namespace Refidomsa.Api.Exceptions;

public class ReglaNegocioException : Exception
{
    public string Codigo { get; }

    public ReglaNegocioException(string codigo, string mensaje) : base(mensaje)
    {
        Codigo = codigo;
    }
}
