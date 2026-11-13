import { computed, ref } from 'vue'
import {
  getAccount,
  getOrders,
  getWishlist,
  placeOrder,
  registerAccount,
  removeFromWishlist,
  saveToWishlist,
  signIn as apiSignIn,
  signOut as apiSignOut,
  type Account,
  type Order,
  type Sneaker,
} from '../api'
import { getEntrantId } from './entrant'
import { useCart } from './useCart'

// Module-level state, so every component that calls useAccount() shares one session.
const account = ref<Account | null>(null)
const wishlist = ref<Sneaker[]>([])
const orders = ref<Order[]>([])
const panelOpen = ref(false)
const panelTab = ref<'orders' | 'saved'>('orders')

const savedIds = computed(() => new Set(wishlist.value.map((sneaker) => sneaker.id)))
const isSaved = (sneakerId: string) => savedIds.value.has(sneakerId)

async function loadMemberData() {
  if (!account.value) {
    wishlist.value = []
    orders.value = []
    return
  }
  const [saved, placed] = await Promise.all([getWishlist(), getOrders()])
  wishlist.value = saved
  orders.value = placed
}

async function load() {
  try {
    account.value = (await getAccount()).account
    await loadMemberData()
  } catch {
    // The store still works signed out if this fails.
  }
}

/** The bag belongs to whoever is signed in, so it has to be fetched again. */
async function identityChanged() {
  await Promise.all([loadMemberData().catch(() => {}), useCart().load()])
}

async function register(email: string, password: string) {
  account.value = await registerAccount(getEntrantId(), email, password)
  await identityChanged()
}

async function signIn(email: string, password: string) {
  account.value = await apiSignIn(getEntrantId(), email, password)
  await identityChanged()
}

async function signOut() {
  await apiSignOut()
  account.value = null
  await identityChanged()
}

/** Resolves to whether it succeeded. Shoppers who aren't signed in are asked to. */
async function toggleWishlist(sneaker: Sneaker) {
  if (!account.value) {
    panelOpen.value = true
    return true
  }
  try {
    wishlist.value = isSaved(sneaker.id)
      ? await removeFromWishlist(sneaker.id)
      : await saveToWishlist(sneaker.id)
    return true
  } catch {
    return false
  }
}

/** Places the order and shows it. Resolves to whether it succeeded. */
async function checkout() {
  try {
    const placed = await placeOrder()
    // The worker can announce progress before this response arrives. If that already put the
    // order on screen, that copy is the newer one.
    if (!orders.value.some((order) => order.id === placed.id)) {
      orders.value = [placed, ...orders.value]
    }
    panelTab.value = 'orders'
    return true
  } catch {
    return false
  } finally {
    // On success the bag is empty. On failure it may have changed under us.
    await useCart().load()
  }
}

/**
 * Reads the orders again. A response can be older than what's on screen, because progress also
 * arrives as events, so a time that's already showing is never replaced by an empty one.
 */
async function refreshOrders() {
  if (!account.value) {
    return
  }
  const fetched = await getOrders()
  const shown = new Map(orders.value.map((order) => [order.id, order]))
  orders.value = fetched.map((order) => {
    const before = shown.get(order.id)
    return {
      ...order,
      receiptSentAt: order.receiptSentAt ?? before?.receiptSentAt ?? null,
      packedAt: order.packedAt ?? before?.packedAt ?? null,
    }
  })
}

/** Records progress that the server just announced for one order. */
async function markOrder(orderId: string, step: 'receiptSentAt' | 'packedAt', at: string) {
  const order = orders.value.find((candidate) => candidate.id === orderId)
  if (order) {
    order[step] ??= at
  } else {
    // The event beat the checkout response, so the order isn't on screen yet.
    await refreshOrders()
  }
}

export function useAccount() {
  return {
    account,
    wishlist,
    orders,
    panelOpen,
    panelTab,
    isSaved,
    load,
    register,
    signIn,
    signOut,
    toggleWishlist,
    checkout,
    refreshOrders,
    markOrder,
  }
}
