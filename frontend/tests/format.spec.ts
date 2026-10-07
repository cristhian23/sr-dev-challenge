import { test, expect } from '@playwright/test';
import { decimal6, siguienteDia, subtotalCentavos } from '../src/format';

test('decimal estimates round half cents per line and preserve six decimals', () => {
  expect(subtotalCentavos('500.005', 1)).toBe(50001n);
  expect(subtotalCentavos('500.005', 1) * 2n).toBe(100002n);
  expect(subtotalCentavos('500.000001', 290.123456)).toBe(14506173n);
  expect(decimal6('9000')).toBe(9000000000n);
  expect(() => decimal6('500.0000001')).toThrow();
  expect(siguienteDia('2026-12-31')).toBe('2027-01-01');
});
