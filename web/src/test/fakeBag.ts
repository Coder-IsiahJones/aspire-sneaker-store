import { vi } from 'vitest'
import type { Account, BagLine, Order, Sneaker } from '../api'
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
  getAccount: vi.fn(async (): Promise<{ account: Account | null }> => ({ account: null })),
  registerAccount: vi.fn(async (_entrant: string, email: string): Promise<Account> => ({ email })),
  signIn: vi.fn(async (_entrant: string, email: string): Promise<Account> => ({ email })),
  signOut: vi.fn(async () => {}),
  getWishlist: vi.fn(async (): Promise<Sneaker[]> => []),
  saveToWishlist: vi.fn(async (sneakerId: string): Promise<Sneaker[]> => [knownSneakers.get(sneakerId)!]),
  removeFromWishlist: vi.fn(async (_sneakerId: string): Promise<Sneaker[]> => []),
  getOrders: vi.fn(async (): Promise<Order[]> => []),
  placeOrder: vi.fn(
    async (): Promise<Order> => ({ id: 'order1', placedAt: '2026-06-01T12:00:00Z', lines: [], total: 0 }),
  ),
}

/** Empties the fake bag and the cart mirroring it. Call from beforeEach. */
export async function resetFakeBag(cart: { lines: BagLine[]; load: () => Promise<boolean> }) {
  bag = []
  vi.clearAllMocks()
  await cart.load()
}
