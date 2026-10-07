export function dinero(value: number) {
  return new Intl.NumberFormat('es-DO', { style: 'currency', currency: 'DOP' }).format(value);
}

export function fecha(value: string) {
  return new Intl.DateTimeFormat('es-DO', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'America/Santo_Domingo',
  }).format(new Date(value));
}

export function siguienteDia(day: string) {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + 1);
  return date.toISOString().slice(0, 10);
}

// Escala de seis decimales; el producto se redondea a centavos por linea, sin floats.
export function decimal6(value: string): bigint {
  if (!/^\d+(\.\d{1,6})?$/.test(value)) throw new Error('Usa hasta seis decimales.');
  const [entero, fraccion = ''] = value.split('.');
  return BigInt(entero) * 1_000_000n + BigInt(fraccion.padEnd(6, '0'));
}

export function subtotalCentavos(galones: string, precio: number): bigint {
  const producto = decimal6(galones) * decimal6(String(precio));
  return (producto + 5_000_000_000n) / 10_000_000_000n;
}
