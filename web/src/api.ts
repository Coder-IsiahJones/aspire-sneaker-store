export interface Sneaker {
  id: string
  sku: string
  name: string
  brand: string
  colorway: string
  price: number
  releaseYear: number
  sizes: number[]
  /** Upper, overlays, sole. */
  colors: string[]
  description: string
}

export interface BagLine {
  key: string
  sneaker: Sneaker
  size: number
  quantity: number
}

export type DropPhase = 'upcoming' | 'open' | 'closed'

export interface DropEntry {
  size: number
  result: 'pending' | 'won' | 'lost'
  /** Whether a won pair has already been ordered. */
  claimed?: boolean
}

export interface Drop {
  id: string
  sneaker: Sneaker
  pairs: number
  opensAt: string
  closesAt: string
  phase: DropPhase
  entries: number
  /** This shopper's entry, if they have one. */
  entry: DropEntry | null
}

export interface DropList {
  serverTime: string
  drops: Drop[]
}

export interface Account {
  email: string
}

export interface OrderLine {
  sneakerId: string
  sku: string
  name: string
  brand: string
  colorway: string
  colors: string[]
  size: number
  quantity: number
  unitPrice: number
}

export interface Order {
  id: string
  placedAt: string
  lines: OrderLine[]
  total: number
  /** When the worker finished each job. Null until it has. The two can arrive in either order. */
  receiptSentAt: string | null
  packedAt: string | null
}

/** The data of an 'order.confirmed' or 'order.packed' event from GET /api/orders/events. */
export interface OrderUpdate {
  orderId: string
  at: string
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

function assertOk(response: Response, path: string) {
  if (!response.ok) {
    throw new ApiError(response.status, `${response.status} ${response.statusText} from ${path}`)
  }
}

async function readJson<T>(response: Response, path: string): Promise<T> {
  assertOk(response, path)
  return response.json() as Promise<T>
}

async function getJson<T>(path: string): Promise<T> {
  // Relative URL: the Vite dev server proxies /api to the C# API.
  return readJson<T>(await fetch(path), path)
}

export function getSneakers(brand?: string) {
  const query = brand ? `?brand=${encodeURIComponent(brand)}` : ''
  return getJson<Sneaker[]>(`/api/sneakers${query}`)
}

export function getBrands() {
  return getJson<string[]>('/api/brands')
}

export async function getBag(entrantId: string) {
  const path = '/api/bag'
  return readJson<BagLine[]>(await fetch(path, { headers: { 'X-Entrant-Id': entrantId } }), path)
}

export async function addToBag(entrantId: string, sneakerId: string, size: number) {
  const path = '/api/bag/items'
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Entrant-Id': entrantId },
    body: JSON.stringify({ sneakerId, size }),
  })
  return readJson<BagLine[]>(response, path)
}

export async function removeFromBag(entrantId: string, key: string) {
  const path = `/api/bag/items/${encodeURIComponent(key)}`
  const response = await fetch(path, {
    method: 'DELETE',
    headers: { 'X-Entrant-Id': entrantId },
  })
  return readJson<BagLine[]>(response, path)
}

export async function getDrops(entrantId: string) {
  const path = '/api/drops'
  return readJson<DropList>(await fetch(path, { headers: { 'X-Entrant-Id': entrantId } }), path)
}

export async function enterDrop(dropId: string, entrantId: string, size: number) {
  const path = `/api/drops/${encodeURIComponent(dropId)}/entries`
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Entrant-Id': entrantId },
    body: JSON.stringify({ size }),
  })
  return readJson<DropEntry>(response, path)
}

export function getAccount() {
  return getJson<{ account: Account | null }>('/api/account')
}

async function sendCredentials(path: string, entrantId: string, email: string, password: string) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Entrant-Id': entrantId },
    body: JSON.stringify({ email, password }),
  })
  return readJson<Account>(response, path)
}

export function registerAccount(entrantId: string, email: string, password: string) {
  return sendCredentials('/api/account/register', entrantId, email, password)
}

export function signIn(entrantId: string, email: string, password: string) {
  return sendCredentials('/api/account/sign-in', entrantId, email, password)
}

export async function signOut() {
  const path = '/api/account/sign-out'
  assertOk(await fetch(path, { method: 'POST' }), path)
}

export function getWishlist() {
  return getJson<Sneaker[]>('/api/wishlist')
}

export async function saveToWishlist(sneakerId: string) {
  const path = `/api/wishlist/${encodeURIComponent(sneakerId)}`
  return readJson<Sneaker[]>(await fetch(path, { method: 'PUT' }), path)
}

export async function removeFromWishlist(sneakerId: string) {
  const path = `/api/wishlist/${encodeURIComponent(sneakerId)}`
  return readJson<Sneaker[]>(await fetch(path, { method: 'DELETE' }), path)
}

export function getOrders() {
  return getJson<Order[]>('/api/orders')
}

export async function placeOrder() {
  const path = '/api/orders'
  return readJson<Order>(await fetch(path, { method: 'POST' }), path)
}
