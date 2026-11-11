import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  getAccount,
  getBag,
  getOrders,
  getWishlist,
  placeOrder,
  registerAccount,
  removeFromWishlist,
  saveToWishlist,
  signIn as apiSignIn,
  signOut as apiSignOut,
  type Order,
} from '../api'
import { resetFakeBag } from '../test/fakeBag'
import { makeSneaker } from '../test/sneaker'
import { getEntrantId } from './entrant'
import { useAccount } from './useAccount'
import { useCart } from './useCart'

vi.mock('../api', async () => (await import('../test/fakeBag')).fakeBagApi)

const order: Order = { id: 'order1', placedAt: '2026-06-01T12:00:00Z', total: 100, lines: [] }

beforeEach(async () => {
  await resetFakeBag(useCart())
  const state = useAccount()
  state.account.value = null
  state.wishlist.value = []
  state.orders.value = []
  state.panelOpen.value = false
  state.panelTab.value = 'saved'
  vi.clearAllMocks()
})

describe('useAccount', () => {
  it('stays signed out and fetches nothing private when there is no session', async () => {
    const { account, load } = useAccount()

    await load()

    expect(account.value).toBeNull()
    expect(getWishlist).not.toHaveBeenCalled()
    expect(getOrders).not.toHaveBeenCalled()
  })

  it('restores a session with its saved shoes and orders', async () => {
    const sneaker = makeSneaker()
    vi.mocked(getAccount).mockResolvedValueOnce({ account: { email: 'me@example.com' } })
    vi.mocked(getWishlist).mockResolvedValueOnce([sneaker])
    vi.mocked(getOrders).mockResolvedValueOnce([order])
    const state = useAccount()

    await state.load()

    expect(state.account.value).toEqual({ email: 'me@example.com' })
    expect(state.isSaved(sneaker.id)).toBe(true)
    expect(state.orders.value).toEqual([order])
  })

  it('survives the account check failing', async () => {
    vi.mocked(getAccount).mockRejectedValueOnce(new Error('down'))

    await expect(useAccount().load()).resolves.toBeUndefined()
  })

  it('sends the browser id when signing in, so its bag can move over', async () => {
    await useAccount().signIn('me@example.com', 'long enough')

    expect(apiSignIn).toHaveBeenCalledWith(getEntrantId(), 'me@example.com', 'long enough')
  })

  it('sends the browser id when registering', async () => {
    await useAccount().register('me@example.com', 'long enough')

    expect(registerAccount).toHaveBeenCalledWith(getEntrantId(), 'me@example.com', 'long enough')
  })

  it('reloads the bag after signing in', async () => {
    await useAccount().signIn('me@example.com', 'long enough')

    expect(getBag).toHaveBeenCalledTimes(1)
  })

  it('leaves the session alone when signing in fails', async () => {
    vi.mocked(apiSignIn).mockRejectedValueOnce(new Error('401'))
    const state = useAccount()

    await expect(state.signIn('me@example.com', 'wrong password')).rejects.toThrow('401')

    expect(state.account.value).toBeNull()
  })

  it('clears private data and reloads the bag after signing out', async () => {
    const state = useAccount()
    state.account.value = { email: 'me@example.com' }
    state.wishlist.value = [makeSneaker()]
    state.orders.value = [order]

    await state.signOut()

    expect(apiSignOut).toHaveBeenCalled()
    expect(state.account.value).toBeNull()
    expect(state.wishlist.value).toEqual([])
    expect(state.orders.value).toEqual([])
    expect(getBag).toHaveBeenCalledTimes(1)
  })

  it('asks a signed-out shopper to sign in instead of saving', async () => {
    const state = useAccount()

    await state.toggleWishlist(makeSneaker())

    expect(state.panelOpen.value).toBe(true)
    expect(saveToWishlist).not.toHaveBeenCalled()
  })

  it('saves and unsaves a shoe', async () => {
    const sneaker = makeSneaker()
    const state = useAccount()
    state.account.value = { email: 'me@example.com' }

    vi.mocked(saveToWishlist).mockResolvedValueOnce([sneaker])
    await state.toggleWishlist(sneaker)
    expect(state.isSaved(sneaker.id)).toBe(true)

    vi.mocked(removeFromWishlist).mockResolvedValueOnce([])
    await state.toggleWishlist(sneaker)
    expect(state.isSaved(sneaker.id)).toBe(false)
  })

  it('reports a failed save', async () => {
    const state = useAccount()
    state.account.value = { email: 'me@example.com' }
    vi.mocked(saveToWishlist).mockRejectedValueOnce(new Error('503'))

    await expect(state.toggleWishlist(makeSneaker())).resolves.toBe(false)
  })

  it('puts a new order first, shows the orders tab and refreshes the bag', async () => {
    const older: Order = { ...order, id: 'older' }
    const state = useAccount()
    state.orders.value = [older]
    vi.mocked(placeOrder).mockResolvedValueOnce(order)

    await expect(state.checkout()).resolves.toBe(true)

    expect(state.orders.value.map((o) => o.id)).toEqual(['order1', 'older'])
    expect(state.panelTab.value).toBe('orders')
    expect(getBag).toHaveBeenCalledTimes(1)
  })

  it('reports a failed checkout and still resyncs the bag', async () => {
    vi.mocked(placeOrder).mockRejectedValueOnce(new Error('409'))

    await expect(useAccount().checkout()).resolves.toBe(false)

    expect(getBag).toHaveBeenCalledTimes(1)
  })
})
