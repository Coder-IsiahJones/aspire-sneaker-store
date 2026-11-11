import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import type { Drop } from '../api'
import { useCart } from '../composables/useCart'
import { inMinutes, makeDrop, NOW } from '../test/drop'
import DropTicket from './DropTicket.vue'

function mountTicket(drop: Drop, props: { entering?: boolean; failed?: boolean } = {}) {
  return mount(DropTicket, {
    props: { drop, now: NOW, entering: false, failed: false, ...props },
  })
}

beforeEach(() => {
  useCart().lines.splice(0)
})

describe('DropTicket', () => {
  it('counts down to opening and offers no entry while upcoming', () => {
    const wrapper = mountTicket(
      makeDrop({ phase: 'upcoming', opensAt: inMinutes(2), closesAt: inMinutes(10) }),
    )

    expect(wrapper.get('[role="timer"]').attributes('aria-label')).toBe('Entries open in')
    expect(wrapper.findAll('.flap').map((flap) => flap.text()).join('')).toBe('000200')
    expect(wrapper.find('button.go').exists()).toBe(false)
  })

  it('counts down to the draw while open', () => {
    const wrapper = mountTicket(makeDrop())

    expect(wrapper.get('[role="timer"]').attributes('aria-label')).toBe('Draw in')
    expect(wrapper.findAll('.flap').map((flap) => flap.text()).join('')).toBe('000500')
  })

  it('shows a days unit for long countdowns', () => {
    const wrapper = mountTicket(
      makeDrop({ phase: 'upcoming', opensAt: inMinutes(60 * 49), closesAt: inMinutes(60 * 72) }),
    )

    expect(wrapper.findAll('.unit-name').map((name) => name.text())).toEqual(['day', 'hr', 'min', 'sec'])
    expect(wrapper.findAll('.flap').map((flap) => flap.text()).join('')).toBe('02010000')
  })

  it('never shows a negative time', () => {
    const wrapper = mountTicket(makeDrop({ closesAt: inMinutes(-1) }))

    expect(wrapper.findAll('.flap').map((flap) => flap.text()).join('')).toBe('000000')
  })

  it('needs a size before entering', async () => {
    const wrapper = mountTicket(makeDrop())
    const go = wrapper.get('button.go')

    expect(go.text()).toBe('Pick a size')
    expect(go.attributes('disabled')).toBeDefined()

    await wrapper.findAll('button.size')[1]!.trigger('click')
    await go.trigger('click')

    expect(wrapper.emitted('enter')).toEqual([[9]])
  })

  it('blocks a second submit while entering', async () => {
    const wrapper = mountTicket(makeDrop(), { entering: true })
    await wrapper.findAll('button.size')[0]!.trigger('click')

    expect(wrapper.get('button.go').attributes('disabled')).toBeDefined()
  })

  it('reports a failed entry', () => {
    const wrapper = mountTicket(makeDrop(), { failed: true })

    expect(wrapper.get('[role="alert"]').text()).toContain("didn't go through")
  })

  it('confirms an entry instead of offering another', () => {
    const wrapper = mountTicket(makeDrop({ entry: { size: 9.5, result: 'pending' } }))

    expect(wrapper.find('button.go').exists()).toBe(false)
    expect(wrapper.get('[role="status"]').text()).toContain("You're in, size 9.5.")
  })

  it('lets a winner add the pair to the bag once', async () => {
    const drop = makeDrop({ phase: 'closed', entry: { size: 10, result: 'won' } })
    const wrapper = mountTicket(drop)

    expect(wrapper.get('.stamp').text()).toBe('Winner')

    await wrapper.get('button.go').trigger('click')

    const { lines } = useCart()
    expect(lines).toHaveLength(1)
    expect(lines[0]).toMatchObject({ size: 10, quantity: 1 })
    expect(wrapper.get('button.go').text()).toBe('In the bag')
    expect(wrapper.get('button.go').attributes('disabled')).toBeDefined()
  })

  it('tells a loser and offers nothing to buy', () => {
    const wrapper = mountTicket(makeDrop({ phase: 'closed', entry: { size: 10, result: 'lost' } }))

    expect(wrapper.get('.stamp').text()).toBe('No pair')
    expect(wrapper.find('button.go').exists()).toBe(false)
    expect(wrapper.get('[role="status"]').text()).toContain('Not this time')
  })

  it('shows a closed drop to someone who never entered', () => {
    const wrapper = mountTicket(makeDrop({ phase: 'closed', pairs: 1 }))

    expect(wrapper.get('.stamp').text()).toBe('Closed')
    expect(wrapper.get('.note').text()).toBe('Entries closed. 1 pair, 7 entered.')
    expect(wrapper.find('[role="timer"]').exists()).toBe(false)
  })

  it('moves the shutter with the phase', () => {
    const height = (drop: Drop) =>
      (mountTicket(drop).get('.shutter').element as HTMLElement).style.height

    expect(height(makeDrop({ phase: 'upcoming' }))).toBe('84%')
    expect(height(makeDrop())).toBe('7%')
    expect(height(makeDrop({ phase: 'closed', entry: { size: 9, result: 'won' } }))).toBe('7%')
    expect(height(makeDrop({ phase: 'closed', entry: { size: 9, result: 'lost' } }))).toBe('100%')
  })
})
