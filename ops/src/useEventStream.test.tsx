import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { FakeEventSource } from './test/fakeEventSource.ts'
import { useEventStream } from './useEventStream.ts'

beforeEach(() => {
  FakeEventSource.opened = []
  vi.stubGlobal('EventSource', FakeEventSource)
})

const stream = () => FakeEventSource.opened.at(-1)!

describe('useEventStream', () => {
  it('is not live until the API says it is ready', () => {
    const { result } = renderHook(() => useEventStream('/api/ops/events'))

    expect(stream().url).toBe('/api/ops/events')
    expect(result.current.live).toBe(false)

    act(() => stream().emit('ready'))

    expect(result.current.live).toBe(true)
  })

  it('lists events newest first and counts each kind', () => {
    const { result } = renderHook(() => useEventStream('/api/ops/events'))

    act(() => {
      stream().emit('order.placed', { orderId: 'a1', email: 'me@example.com' })
      stream().emit('order.confirmed', { orderId: 'a1' })
      stream().emit('order.placed', { orderId: 'b2', email: 'you@example.com' })
    })

    expect(result.current.events.map((event) => `${event.key} ${event.orderId}`)).toEqual([
      'order.placed b2',
      'order.confirmed a1',
      'order.placed a1',
    ])
    expect(result.current.events[0]!.email).toBe('you@example.com')
    expect(result.current.tally).toEqual({ 'order.placed': 2, 'order.confirmed': 1, 'order.packed': 0 })
  })

  it('keeps only the newest lines but still counts every event', () => {
    const { result } = renderHook(() => useEventStream('/api/ops/events', 2))

    act(() => {
      for (const orderId of ['a', 'b', 'c']) {
        stream().emit('order.packed', { orderId })
      }
    })

    expect(result.current.events.map((event) => event.orderId)).toEqual(['c', 'b'])
    expect(result.current.tally['order.packed']).toBe(3)
  })

  it('shows as disconnected after an error, and live again on the next ready', () => {
    const { result } = renderHook(() => useEventStream('/api/ops/events'))

    act(() => stream().emit('ready'))
    act(() => stream().emit('error'))
    expect(result.current.live).toBe(false)

    // EventSource reconnects by itself, and the API greets every connection with 'ready'.
    act(() => stream().emit('ready'))
    expect(result.current.live).toBe(true)
  })

  it('closes the stream when the component unmounts', () => {
    const { unmount } = renderHook(() => useEventStream('/api/ops/events'))
    const opened = stream()

    unmount()

    expect(opened.closed).toBe(true)
  })
})
