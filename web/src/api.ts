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
}

export interface Drop {
  id: string
  sneaker: Sneaker
  pairs: number
  opensAt: string
  closesAt: string
  phase: DropPhase
  entries: number
  /** This browser's entry, if it has one. */
  entry: DropEntry | null
}

export interface DropList {
  serverTime: string
  drops: Drop[]
}

async function readJson<T>(response: Response, path: string): Promise<T> {
  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText} from ${path}`)
  }
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
