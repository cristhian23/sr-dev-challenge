import type { Producto } from '../types/productos';

export interface LineaForm { productoId: string; galones: string }

export function PedidoLineasForm({ productos, lineas, onChange }: {
  productos: Producto[];
  lineas: LineaForm[];
  onChange: (lineas: LineaForm[]) => void;
}) {
  function cambiar(index: number, campo: keyof LineaForm, value: string) {
    onChange(lineas.map((linea, i) => i === index ? { ...linea, [campo]: value } : linea));
  }
  return <div className="order-lines">
    {lineas.map((linea, i) => <div className="line" key={i}>
      <label>Producto {i + 1}<select aria-label={`Producto ${i + 1}`} value={linea.productoId} required onChange={e => cambiar(i, 'productoId', e.target.value)}>
        <option value="">Seleccionar producto</option>
        {productos.map(p => <option key={p.id} value={p.id}
          disabled={lineas.some((l, j) => j !== i && l.productoId === p.id)}>{p.nombre}</option>)}
      </select></label>
      <label>Galones {i + 1}<input type="number" min="500" max="9000" step="0.000001" required
        value={linea.galones} onChange={e => cambiar(i, 'galones', e.target.value)} /></label>
      <button type="button" className="secondary" aria-label={`Quitar linea ${i + 1}`}
        disabled={lineas.length === 1} onClick={() => onChange(lineas.filter((_, j) => j !== i))}>Quitar</button>
    </div>)}
    <button type="button" className="secondary" disabled={lineas.length === 4}
      onClick={() => onChange([...lineas, { productoId: '', galones: '500' }])}>Agregar linea</button>
  </div>;
}
