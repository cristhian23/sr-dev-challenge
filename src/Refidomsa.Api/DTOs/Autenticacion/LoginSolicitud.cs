using System.ComponentModel.DataAnnotations;

namespace Refidomsa.Api.DTOs.Autenticacion;

public class LoginSolicitud
{
    [Required]
    public string NombreUsuario { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
