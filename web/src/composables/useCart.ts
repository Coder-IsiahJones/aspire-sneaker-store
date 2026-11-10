import { computed, reactive } from 'vue'
import type { Sneaker } from '../api'

export interface CartLine {
  key: string
  sneaker: Sneaker
  size: number
  quantity: number
}

// Module-level state, so every component that calls useCart() shares one cart.
const lines = reactive<CartLine[]>([])

const count = computed(() => lines.reduce((sum, line) => sum + line.quantity, 0))
const total = computed(() =>
  lines.reduce((sum, line) => sum + line.sneaker.price * line.quantity, 0),
)

function add(sneaker: Sneaker, size: number) {
  const key = `${sneaker.id}:${size}`
  const existing = lines.find((line) => line.key === key)
  if (existing) {
    existing.quantity++
  } else {
    lines.push({ key, sneaker, size, quantity: 1 })
  }
}

function remove(key: string) {
  const index = lines.findIndex((line) => line.key === key)
  if (index !== -1) {
    lines.splice(index, 1)
  }
}

export function useCart() {
  return { lines, count, total, add, remove }
}

export const formatPrice = (value: number) =>
  new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 0,
  }).format(value)
