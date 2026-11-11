import type { Sneaker } from '../api'

let nextId = 1

/** A sneaker with sensible defaults. Pass only the fields a test cares about. */
export function makeSneaker(overrides: Partial<Sneaker> = {}): Sneaker {
  const id = String(nextId++).padStart(24, '0')
  return {
    id,
    sku: `TST-${id.slice(-4)}`,
    name: 'Test Runner',
    brand: 'Testbrand',
    colorway: 'Lab White',
    price: 120,
    releaseYear: 2020,
    sizes: [8, 9, 9.5, 10],
    colors: ['#FFFFFF', '#000000', '#FFFFFF'],
    description: 'A shoe that only exists in tests.',
    ...overrides,
  }
}
