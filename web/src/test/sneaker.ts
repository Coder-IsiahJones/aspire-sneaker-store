import type { Sneaker } from '../api'

let nextId = 1

/** Every sneaker made in this test file, so the fake bag can look them up by id. */
export const knownSneakers = new Map<string, Sneaker>()

/** A sneaker with sensible defaults. Pass only the fields a test cares about. */
export function makeSneaker(overrides: Partial<Sneaker> = {}): Sneaker {
  const id = String(nextId++).padStart(24, '0')
  const sneaker = {
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
  knownSneakers.set(sneaker.id, sneaker)
  return sneaker
}
