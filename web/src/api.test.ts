import { describe, expect, it, vi } from 'vitest'
import { enterDrop, getBrands, getDrops, getSneakers } from './api'
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
