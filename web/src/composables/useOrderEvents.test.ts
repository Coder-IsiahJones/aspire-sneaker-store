import { effectScope, nextTick, type EffectScope } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getOrders, type Order } from '../api'
import { resetFakeBag } from '../test/fakeBag'
import { useAccount } from './useAccount'
import { useCart } from './useCart'
import { useOrderEvents } from './useOrderEvents'

vi.mock('../api', async () => (await import('../test/fakeBag')).fakeBagApi)

/** Stands in for the browser's EventSource, so a test can push events through it. */
class FakeEventSource {
  static opened: FakeEventSource[] = []

  readonly url: string
  closed = false
  private readonly listeners = new Map<string, ((event: MessageEvent) => void)[]>()

  constructor(url: string) {
    this.url = url
    FakeEventSource.opened.push(this)
  }

  addEventListener(type: string, listener: (event: MessageEvent) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener])
  }

  close() {
    this.closed = true
  }

  emit(type: string, data: unknown = null) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({ data: JSON.stringify(data) } as MessageEvent)
    }
  }
}

const order: Order = {
  id: 'order1',
  placedAt: '2026-06-01T12:00:00Z',
  total: 100,
  lines: [],
  receiptSentAt: null,
  packedAt: null,
}

let scope: EffectScope

/** Runs the composable the way a component would, and returns the stream it opened, if any. */
async function listen() {
  scope = effectScope()
  scope.run(() => useOrderEvents())
  await nextTick()
  return FakeEventSource.opened.at(-1)
}

beforeEach(async () => {
  await resetFakeBag(useCart())
  FakeEventSource.opened = []
  vi.stubGlobal('EventSource', FakeEventSource)
  const state = useAccount()
  state.account.value = null
  state.orders.value = []
})

afterEach(() => scope?.stop())

describe('useOrderEvents', () => {
  it('opens no stream while signed out', async () => {
    expect(await listen()).toBeUndefined()
  })

  it('opens the stream on sign-in and closes it on sign-out', async () => {
    const { account } = useAccount()
    await listen()

    account.value = { email: 'me@example.com' }
    await nextTick()
    const stream = FakeEventSource.opened.at(-1)!

    expect(stream.url).toBe('/api/orders/events')
    expect(stream.closed).toBe(false)

    account.value = null
    await nextTick()

    expect(stream.closed).toBe(true)
    expect(FakeEventSource.opened).toHaveLength(1)
  })

  it('reads the orders again every time the stream says it is ready', async () => {
    const state = useAccount()
    state.account.value = { email: 'me@example.com' }
    const stream = (await listen())!
    vi.mocked(getOrders).mockResolvedValue([{ ...order, packedAt: '2026-06-01T12:00:05Z' }])

    stream.emit('ready')
    await vi.waitFor(() => expect(state.orders.value[0]?.packedAt).toBe('2026-06-01T12:00:05Z'))

    // A reconnect sends 'ready' again.
    stream.emit('ready')
    await vi.waitFor(() => expect(getOrders).toHaveBeenCalledTimes(2))
  })

  it('ticks the right step when the worker reports in', async () => {
    const state = useAccount()
    state.account.value = { email: 'me@example.com' }
    state.orders.value = [{ ...order }]
    const stream = (await listen())!

    stream.emit('order.packed', { orderId: 'order1', at: '2026-06-01T12:00:05Z' })
    stream.emit('order.confirmed', { orderId: 'order1', at: '2026-06-01T12:00:07Z' })

    expect(state.orders.value[0]).toMatchObject({
      packedAt: '2026-06-01T12:00:05Z',
      receiptSentAt: '2026-06-01T12:00:07Z',
    })
  })

  it('closes the stream when its component goes away', async () => {
    useAccount().account.value = { email: 'me@example.com' }
    const stream = (await listen())!

    scope.stop()

    expect(stream.closed).toBe(true)
  })
})
