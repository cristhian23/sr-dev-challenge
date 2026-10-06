using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Refidomsa.Api.DTOs.Pedidos;

public class GalonesJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException("Galones debe ser un numero decimal.");
        }

        using var documento = JsonDocument.ParseValue(ref reader);
        var texto = documento.RootElement.GetRawText();
        if (!TieneEscalaPermitida(texto) || !documento.RootElement.TryGetDecimal(out var valor))
        {
            throw new JsonException("Galones admite hasta seis decimales, sin perdida de precision.");
        }
        return valor;
    }

    private static bool TieneEscalaPermitida(string texto)
    {
        int posicionExponente = texto.IndexOfAny(new[] { 'e', 'E' });
        int exponente = 0;
        var mantisa = texto;
        if (posicionExponente >= 0)
        {
            mantisa = texto[..posicionExponente];
            if (!int.TryParse(texto[(posicionExponente + 1)..], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponente))
            {
                return false;
            }
        }

        int punto = mantisa.IndexOf('.');
        int decimales = punto < 0 ? 0 : mantisa.Length - punto - 1;
        var digitos = mantisa.Replace(".", "").TrimStart('-');
        if (digitos.All(digito => digito == '0'))
        {
            return true;
        }
        int cerosFinales = digitos.Length - digitos.TrimEnd('0').Length;
        // Examinar el token antes de convertir evita el redondeo silencioso de CLR decimal.
        return (long)decimales - exponente - cerosFinales <= 6;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
