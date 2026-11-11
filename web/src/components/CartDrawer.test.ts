import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { removeFromBag } from '../api'
import { useCart } from '../composables/useCart'
import { resetFakeBag } from '../test/fakeBag'
import { makeSneaker } from '../test/sneaker'
import CartDrawer from './CartDrawer.vue'

vi.mock('../api', async () => (await import('../test/fakeBag')).fakeBagApi)

beforeEach(() => resetFakeBag(useCart()))

let wrapper: VueWrapper | undefined

afterEach(() => {
  // Unmounting removes the window keydown listener.
  wrapper?.unmount()
  wrapper = undefined
})

function mountDrawer(open = true): VueWrapper {
  wrapper = mount(CartDrawer, {
    props: { modelValue: open, 'onUpdate:modelValue': () => {} },
  })
  return wrapper
}

describe('CartDrawer', () => {
  it('renders nothing while closed', () => {
    const drawer = mountDrawer(false)

    expect(drawer.find('[role="dialog"]').exists()).toBe(false)
  })

  it('says the bag is empty when there is nothing in it', () => {
    const drawer = mountDrawer()

    expect(drawer.get('.empty').text()).toBe('Nothing in the bag yet.')
    expect(drawer.get('.meta').text()).toBe('0 pairs')
    expect(drawer.get('.total').text()).toContain('$0')
  })

  it('lists each line with its size, quantity and subtotal', async () => {
    const { add } = useCart()
    const dunk = makeSneaker({ name: 'Dunk Low', colorway: 'Panda', price: 115 })
    await add(dunk, 10)
    await add(dunk, 10)

    const drawer = mountDrawer()
    const line = drawer.get('.line')

    expect(line.get('.line-name').text()).toBe('Dunk Low')
    expect(line.get('.line-meta').text()).toBe('Panda · US 10 · x2')
    expect(line.get('.line-right span').text()).toBe('$230')
    expect(drawer.get('.total').text()).toContain('$230')
  })

  it('uses the singular for one pair', async () => {
    await useCart().add(makeSneaker(), 9)

    expect(mountDrawer().get('.meta').text()).toBe('1 pair')
  })

  it('removes a line when its remove button is clicked', async () => {
    const { add, lines } = useCart()
    await add(makeSneaker({ name: 'Keep me' }), 9)
    await add(makeSneaker({ name: 'Drop me' }), 9)
    const drawer = mountDrawer()

    await drawer.findAll('.line')[1]!.get('button.remove').trigger('click')
    await flushPromises()

    expect(lines.map((line) => line.sneaker.name)).toEqual(['Keep me'])
    expect(drawer.findAll('.line')).toHaveLength(1)
  })

  it('says so when a remove fails', async () => {
    await useCart().add(makeSneaker(), 9)
    vi.mocked(removeFromBag).mockRejectedValueOnce(new Error('503'))
    const drawer = mountDrawer()

    await drawer.get('button.remove').trigger('click')
    await flushPromises()

    expect(drawer.findAll('.line')).toHaveLength(1)
    expect(drawer.get('[role="alert"]').text()).toContain("Couldn't remove")
  })

  it.each([
    ['the close button', (drawer: VueWrapper) => drawer.get('button.close').trigger('click')],
    ['the backdrop', (drawer: VueWrapper) => drawer.get('.scrim').trigger('click')],
  ])('closes when %s is clicked', async (_, click) => {
    const drawer = mountDrawer()

    await click(drawer)

    expect(drawer.emitted('update:modelValue')).toEqual([[false]])
  })

  it('closes on Escape', () => {
    const drawer = mountDrawer()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))

    expect(drawer.emitted('update:modelValue')).toEqual([[false]])
  })

  it('ignores other keys', () => {
    const drawer = mountDrawer()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))

    expect(drawer.emitted('update:modelValue')).toBeUndefined()
  })

  it('stops listening for Escape once unmounted', () => {
    const onUpdate = vi.fn()
    const drawer = mount(CartDrawer, {
      props: { modelValue: true, 'onUpdate:modelValue': onUpdate },
    })

    drawer.unmount()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))

    expect(onUpdate).not.toHaveBeenCalled()
  })
})
