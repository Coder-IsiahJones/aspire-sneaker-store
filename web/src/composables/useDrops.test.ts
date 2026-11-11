import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { enterDrop, getDrops, type Drop } from '../api'
import { inMinutes, makeDrop, NOW } from '../test/drop'
import { getEntrantId } from './entrant'
import { useAccount } from './useAccount'
import { useDrops } from './useDrops'

vi.mock('../api', () => ({
  getDrops: vi.fn(),
  enterDrop: vi.fn(),
}))

const respond = (drops: Drop[], serverTime = NOW) =>
  vi.mocked(getDrops).mockResolvedValue({ serverTime: new Date(serverTime).toISOString(), drops })

function mountDrops() {
  let state!: ReturnType<typeof useDrops>
  const wrapper = mount(
    defineComponent({
      setup() {
        state = useDrops()
        return () => h('div')
      },
    }),
  )
  return { wrapper, state }
}

beforeEach(() => {
  vi.useFakeTimers({ now: NOW })
  vi.mocked(getDrops).mockReset()
  vi.mocked(enterDrop).mockReset()
  localStorage.clear()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('getEntrantId', () => {
  it('creates a 32-character hex id and keeps it', () => {
    const id = getEntrantId()

    expect(id).toMatch(/^[0-9a-f]{32}$/)
    expect(getEntrantId()).toBe(id)
  })

  it('still returns a stable id when storage is blocked', () => {
    const blocked = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(getEntrantId()).toBe(getEntrantId())
    blocked.mockRestore()
  })
})

describe('useDrops', () => {
  it('loads drops with the entrant id', async () => {
    const drop = makeDrop()
    respond([drop])

    const { wrapper, state } = mountDrops()
    await flushPromises()

    expect(getDrops).toHaveBeenCalledWith(getEntrantId())
    expect(state.drops.value).toEqual([drop])
    wrapper.unmount()
  })

  it('runs the clock on server time, not the browser clock', async () => {
    respond([makeDrop()], NOW + 90_000)

    const { wrapper, state } = mountDrops()
    await flushPromises()
    expect(state.now.value).toBe(NOW + 90_000)

    await vi.advanceTimersByTimeAsync(1000)
    expect(state.now.value).toBe(NOW + 91_000)
    wrapper.unmount()
  })

  it('refetches just after a countdown reaches zero', async () => {
    respond([makeDrop({ phase: 'upcoming', opensAt: inMinutes(0.1), closesAt: inMinutes(10) })])

    const { wrapper } = mountDrops()
    await flushPromises()
    expect(getDrops).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(6000)
    expect(getDrops).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(400)
    expect(getDrops).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('polls when no boundary is near', async () => {
    respond([makeDrop({ phase: 'closed', opensAt: inMinutes(-9), closesAt: inMinutes(-1) })])

    const { wrapper } = mountDrops()
    await flushPromises()

    await vi.advanceTimersByTimeAsync(30_000)
    expect(getDrops).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('keeps showing drops and retries when a refresh fails', async () => {
    const drop = makeDrop({ phase: 'closed', opensAt: inMinutes(-9), closesAt: inMinutes(-1) })
    respond([drop])
    const { wrapper, state } = mountDrops()
    await flushPromises()

    vi.mocked(getDrops).mockRejectedValue(new Error('down'))
    await vi.advanceTimersByTimeAsync(30_000)
    expect(state.drops.value).toEqual([drop])

    respond([])
    await vi.advanceTimersByTimeAsync(30_000)
    expect(state.drops.value).toEqual([])
    wrapper.unmount()
  })

  it('refetches when the signed-in account changes', async () => {
    respond([makeDrop()])
    const { wrapper } = mountDrops()
    await flushPromises()
    expect(getDrops).toHaveBeenCalledTimes(1)

    useAccount().account.value = { email: 'me@example.com' }
    await flushPromises()

    expect(getDrops).toHaveBeenCalledTimes(2)
    useAccount().account.value = null
    wrapper.unmount()
  })

  it('stops refreshing once unmounted', async () => {
    respond([makeDrop()])
    const { wrapper } = mountDrops()
    await flushPromises()

    wrapper.unmount()
    await vi.advanceTimersByTimeAsync(120_000)

    expect(getDrops).toHaveBeenCalledTimes(1)
  })

  it('enters and refreshes to show the entry', async () => {
    const drop = makeDrop()
    respond([drop])
    vi.mocked(enterDrop).mockResolvedValue({ size: 9, result: 'pending' })
    const { wrapper, state } = mountDrops()
    await flushPromises()

    const entered = { ...drop, entries: 8, entry: { size: 9, result: 'pending' as const } }
    respond([entered])
    await state.enter(drop.id, 9)

    expect(enterDrop).toHaveBeenCalledWith(drop.id, getEntrantId(), 9)
    expect(state.drops.value).toEqual([entered])
    expect(state.failed.value).toBeUndefined()
    expect(state.entering.value).toBeUndefined()
    wrapper.unmount()
  })

  it('marks the drop failed and resyncs when entering fails', async () => {
    const drop = makeDrop()
    respond([drop])
    vi.mocked(enterDrop).mockRejectedValue(new Error('409 Conflict'))
    const { wrapper, state } = mountDrops()
    await flushPromises()

    await state.enter(drop.id, 9)

    expect(state.failed.value).toBe(drop.id)
    expect(getDrops).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })
})
