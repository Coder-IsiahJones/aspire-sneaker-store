import type { Drop } from '../api'
import { makeSneaker } from './sneaker'

let nextId = 1

export const NOW = Date.parse('2026-06-01T12:00:00Z')

const minutes = (count: number) => new Date(NOW + count * 60_000).toISOString()

/** An open drop with sensible defaults. Pass only the fields a test cares about. */
export function makeDrop(overrides: Partial<Drop> = {}): Drop {
  return {
    id: `drop${String(nextId++).padStart(20, '0')}`,
    sneaker: makeSneaker({ name: 'Limited Runner' }),
    pairs: 3,
    opensAt: minutes(-5),
    closesAt: minutes(5),
    phase: 'open',
    entries: 7,
    entry: null,
    ...overrides,
  }
}

export const inMinutes = minutes
