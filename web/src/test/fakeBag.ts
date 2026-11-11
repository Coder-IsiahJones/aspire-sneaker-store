import { vi } from 'vitest'
import type { BagLine } from '../api'
import { knownSneakers } from './sneaker'

let bag: BagLine[] = []

const snapshot = () => bag.map((line) => ({ ...line }))

/** In-memory stand-ins for the API's bag calls, for use in a vi.mock factory. */
export const fakeBagApi = {
  getBag: vi.fn(async () => snapshot()),
  addToBag: vi.fn(async (_entrant: string, sneakerId: string, size: number) => {
    const key = `${sneakerId}:${size}`
    const existing = bag.find((line) => line.key === key)
    if (existing) {
      existing.quantity++
    } else {
      bag.push({ key, sneaker: knownSneakers.get(sneakerId)!, size, quantity: 1 })
    }
    return snapshot()
  }),
  removeFromBag: vi.fn(async (_entrant: string, key: string) => {
    bag = bag.filter((line) => line.key !== key)
    return snapshot()
  }),
}

/** Empties the fake bag and the cart mirroring it. Call from beforeEach. */
export async function resetFakeBag(cart: { lines: BagLine[]; load: () => Promise<boolean> }) {
  bag = []
  vi.clearAllMocks()
  await cart.load()
}
