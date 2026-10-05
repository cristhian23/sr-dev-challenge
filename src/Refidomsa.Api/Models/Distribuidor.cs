namespace Refidomsa.Api.Models;

public class Distribuidor
{
    public Guid Id { get; private set; }
    public string Nombre { get; private set; }
    public string Rnc { get; private set; }
    public decimal LimiteCredito { get; private set; }

    private Distribuidor()
    {
        Nombre = string.Empty;
        Rnc = string.Empty;
    }

    public Distribuidor(Guid id, string nombre, string rnc, decimal limiteCredito)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(rnc);
        if (id == Guid.Empty || limiteCredito < 0)
        {
            throw new ArgumentException("El identificador y limite de credito deben ser validos.");
        }
        Id = id;
        Nombre = nombre.Trim();
        Rnc = rnc.Trim();
        LimiteCredito = limiteCredito;
    }
}
