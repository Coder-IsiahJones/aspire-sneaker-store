import { computed, reactive } from 'vue'
import { addToBag, getBag, removeFromBag, type BagLine, type Sneaker } from '../api'
import { getEntrantId } from './useDrops'

export type CartLine = BagLine

// Module-level state, so every component that calls useCart() shares one cart.
// The bag itself lives in Redis, and this mirrors the API's latest answer.
const lines = reactive<CartLine[]>([])

let latestRequest = 0

const count = computed(() => lines.reduce((sum, line) => sum + line.quantity, 0))
const total = computed(() =>
  lines.reduce((sum, line) => sum + line.sneaker.price * line.quantity, 0),
)

/** Runs a bag request and shows its result. Resolves to whether it succeeded. */
async function sync(request: () => Promise<CartLine[]>) {
  const id = ++latestRequest
  try {
    const result = await request()
    // A slow response must not overwrite a newer one.
    if (id === latestRequest) lines.splice(0, lines.length, ...result)
    return true
  } catch {
    return false
  }
}

const load = () => sync(() => getBag(getEntrantId()))
const add = (sneaker: Sneaker, size: number) =>
  sync(() => addToBag(getEntrantId(), sneaker.id, size))
const remove = (key: string) => sync(() => removeFromBag(getEntrantId(), key))

export function useCart() {
  return { lines, count, total, load, add, remove }
}

export const formatPrice = (value: number) =>
  new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 0,
  }).format(value)
