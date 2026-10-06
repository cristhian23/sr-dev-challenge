namespace Refidomsa.Api.DTOs.Pedidos;

public class ListaPedidosRespuesta
{
    public List<PedidoResumenRespuesta> Items { get; set; } = new List<PedidoResumenRespuesta>();
    public int Pagina { get; set; }
    public int TamanoPagina { get; set; }
    public int TotalRegistros { get; set; }
}
