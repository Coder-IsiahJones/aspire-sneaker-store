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

async function getJson<T>(path: string): Promise<T> {
  // Relative URL: the Vite dev server proxies /api to the C# API.
  const response = await fetch(path)
  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText} from ${path}`)
  }
  return response.json() as Promise<T>
}

export function getSneakers(brand?: string) {
  const query = brand ? `?brand=${encodeURIComponent(brand)}` : ''
  return getJson<Sneaker[]>(`/api/sneakers${query}`)
}

export function getBrands() {
  return getJson<string[]>('/api/brands')
}
