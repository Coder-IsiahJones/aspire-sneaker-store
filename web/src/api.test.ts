import { describe, expect, it, vi } from 'vitest'
import {
  addToBag,
  ApiError,
  enterDrop,
  getAccount,
  getBag,
  getBrands,
  getDrops,
  getOrders,
  getSneakers,
  getWishlist,
  placeOrder,
  registerAccount,
  removeFromBag,
  removeFromWishlist,
  saveToWishlist,
  signIn,
  signOut,
} from './api'
import { makeSneaker } from './test/sneaker'

function stubFetch(response: Response) {
  const fetch = vi.fn().mockResolvedValue(response)
  vi.stubGlobal('fetch', fetch)
  return fetch
}

const json = (body: unknown) =>
  new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })

describe('getSneakers', () => {
  it('requests every sneaker when no brand is given', async () => {
    const sneakers = [makeSneaker()]
    const fetch = stubFetch(json(sneakers))

    await expect(getSneakers()).resolves.toEqual(sneakers)
    expect(fetch).toHaveBeenCalledWith('/api/sneakers')
  })

  it('treats an empty brand as no brand', async () => {
    const fetch = stubFetch(json([]))

    await getSneakers('')

    expect(fetch).toHaveBeenCalledWith('/api/sneakers')
  })

  it('URL-encodes the brand', async () => {
    const fetch = stubFetch(json([]))

    await getSneakers('New Balance & Co')

    expect(fetch).toHaveBeenCalledWith('/api/sneakers?brand=New%20Balance%20%26%20Co')
  })

  it('throws with the status and path when the API fails', async () => {
    stubFetch(new Response('nope', { status: 503, statusText: 'Service Unavailable' }))

    await expect(getSneakers('Nike')).rejects.toThrow(
      '503 Service Unavailable from /api/sneakers?brand=Nike',
    )
  })
})

describe('getBrands', () => {
  it('returns the brand list', async () => {
    const fetch = stubFetch(json(['adidas', 'Nike']))

    await expect(getBrands()).resolves.toEqual(['adidas', 'Nike'])
    expect(fetch).toHaveBeenCalledWith('/api/brands')
  })

  it('throws when the API returns 404', async () => {
    stubFetch(new Response(null, { status: 404, statusText: 'Not Found' }))

    await expect(getBrands()).rejects.toThrow('404 Not Found from /api/brands')
  })
})

describe('bag', () => {
  it('reads the bag with the entrant header', async () => {
    const fetch = stubFetch(json([]))

    await expect(getBag('abc123')).resolves.toEqual([])
    expect(fetch).toHaveBeenCalledWith('/api/bag', { headers: { 'X-Entrant-Id': 'abc123' } })
  })

  it('posts the sneaker and size', async () => {
    const fetch = stubFetch(json([]))

    await addToBag('abc123', 'shoe1', 9.5)

    expect(fetch).toHaveBeenCalledWith('/api/bag/items', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Entrant-Id': 'abc123' },
      body: JSON.stringify({ sneakerId: 'shoe1', size: 9.5 }),
    })
  })

  it('URL-encodes the line key when removing', async () => {
    const fetch = stubFetch(json([]))

    await removeFromBag('abc123', 'shoe1:9.5')

    expect(fetch).toHaveBeenCalledWith('/api/bag/items/shoe1%3A9.5', {
      method: 'DELETE',
      headers: { 'X-Entrant-Id': 'abc123' },
    })
  })

  it('throws when the API refuses', async () => {
    stubFetch(new Response(null, { status: 403, statusText: 'Forbidden' }))

    await expect(addToBag('abc123', 'shoe1', 9)).rejects.toThrow('403 Forbidden from /api/bag/items')
  })
})

describe('account', () => {
  it('reads the session', async () => {
    const fetch = stubFetch(json({ account: null }))

    await expect(getAccount()).resolves.toEqual({ account: null })
    expect(fetch).toHaveBeenCalledWith('/api/account')
  })

  it.each([
    ['/api/account/sign-in', signIn],
    ['/api/account/register', registerAccount],
  ])('posts credentials to %s with the browser id', async (path, call) => {
    const fetch = stubFetch(json({ email: 'a@b.co' }))

    await call('abc123', 'a@b.co', 'long enough')

    expect(fetch).toHaveBeenCalledWith(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Entrant-Id': 'abc123' },
      body: JSON.stringify({ email: 'a@b.co', password: 'long enough' }),
    })
  })

  it('exposes the status of a refused sign-in', async () => {
    stubFetch(new Response(null, { status: 401, statusText: 'Unauthorized' }))

    const error = await signIn('abc123', 'a@b.co', 'wrong password').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(401)
  })

  it('signs out without expecting a body', async () => {
    const fetch = stubFetch(new Response(null, { status: 204 }))

    await expect(signOut()).resolves.toBeUndefined()
    expect(fetch).toHaveBeenCalledWith('/api/account/sign-out', { method: 'POST' })
  })

  it('reads the wishlist and orders', async () => {
    const fetch = vi.fn().mockImplementation(async () => json([]))
    vi.stubGlobal('fetch', fetch)

    await getWishlist()
    await getOrders()

    expect(fetch.mock.calls).toEqual([['/api/wishlist'], ['/api/orders']])
  })

  it('saves and removes wishlist items', async () => {
    const fetch = vi.fn().mockImplementation(async () => json([]))
    vi.stubGlobal('fetch', fetch)

    await saveToWishlist('shoe1')
    await removeFromWishlist('shoe1')

    expect(fetch.mock.calls).toEqual([
      ['/api/wishlist/shoe1', { method: 'PUT' }],
      ['/api/wishlist/shoe1', { method: 'DELETE' }],
    ])
  })

  it('places an order', async () => {
    const fetch = stubFetch(json({ id: 'order1' }))

    await placeOrder()

    expect(fetch).toHaveBeenCalledWith('/api/orders', { method: 'POST' })
  })
})

describe('getDrops', () => {
  it('sends the entrant id as a header, not in the URL', async () => {
    const body = { serverTime: '2026-06-01T12:00:00Z', drops: [] }
    const fetch = stubFetch(json(body))

    await expect(getDrops('abc123')).resolves.toEqual(body)
    expect(fetch).toHaveBeenCalledWith('/api/drops', { headers: { 'X-Entrant-Id': 'abc123' } })
  })
})

describe('enterDrop', () => {
  it('posts the size for the entrant', async () => {
    const fetch = stubFetch(json({ size: 9.5, result: 'pending' }))

    await expect(enterDrop('drop1', 'abc123', 9.5)).resolves.toEqual({ size: 9.5, result: 'pending' })
    expect(fetch).toHaveBeenCalledWith('/api/drops/drop1/entries', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Entrant-Id': 'abc123' },
      body: JSON.stringify({ size: 9.5 }),
    })
  })

  it('throws when the drop refuses the entry', async () => {
    stubFetch(new Response(null, { status: 409, statusText: 'Conflict' }))

    await expect(enterDrop('drop1', 'abc123', 9)).rejects.toThrow(
      '409 Conflict from /api/drops/drop1/entries',
    )
  })
})
