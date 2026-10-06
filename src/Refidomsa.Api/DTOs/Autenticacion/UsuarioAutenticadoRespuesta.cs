namespace Refidomsa.Api.DTOs.Autenticacion;

public class UsuarioAutenticadoRespuesta
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string NombreUsuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public Guid? DistribuidorId { get; set; }
}
