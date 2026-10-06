namespace Refidomsa.Api.DTOs.Autenticacion;

public class LoginRespuesta
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiraEnUtc { get; set; }
    public UsuarioAutenticadoRespuesta Usuario { get; set; } = new UsuarioAutenticadoRespuesta();
}
