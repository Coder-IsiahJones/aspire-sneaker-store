import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, registerAccount, signIn, type Order } from '../api'
import { useAccount } from '../composables/useAccount'
import { useCart } from '../composables/useCart'
import { resetFakeBag } from '../test/fakeBag'
import { makeSneaker } from '../test/sneaker'
import AccountDrawer from './AccountDrawer.vue'

vi.mock('../api', async () => {
  const actual = await vi.importActual<typeof import('../api')>('../api')
  return { ApiError: actual.ApiError, ...(await import('../test/fakeBag')).fakeBagApi }
})

let wrapper: VueWrapper | undefined

beforeEach(async () => {
  await resetFakeBag(useCart())
  const state = useAccount()
  state.account.value = null
  state.wishlist.value = []
  state.orders.value = []
  state.panelTab.value = 'orders'
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

function mountDrawer() {
  wrapper = mount(AccountDrawer, { props: { modelValue: true, 'onUpdate:modelValue': () => {} } })
  return wrapper
}

async function fillAndSubmit(drawer: VueWrapper, email = 'a@b.co', password = 'long enough') {
  await drawer.get('input[type="email"]').setValue(email)
  await drawer.get('input[type="password"]').setValue(password)
  await drawer.get('form').trigger('submit')
  await flushPromises()
}

const order: Order = {
  id: '0000000000000000abc123',
  placedAt: '2026-06-01T12:00:00Z',
  total: 230,
  lines: [
    {
      sneakerId: 's1',
      sku: 'SKU',
      name: 'Dunk Low',
      brand: 'Nike',
      colorway: 'Panda',
      colors: [],
      size: 10,
      quantity: 2,
      unitPrice: 115,
    },
  ],
}

describe('AccountDrawer signed out', () => {
  it('signs in with what was typed', async () => {
    const drawer = mountDrawer()

    await fillAndSubmit(drawer, 'me@example.com', 'hunter2hunter2')

    expect(vi.mocked(signIn).mock.calls[0]!.slice(1)).toEqual(['me@example.com', 'hunter2hunter2'])
    expect(drawer.get('.holder').text()).toBe('me@example.com')
  })

  it('registers after switching modes', async () => {
    const drawer = mountDrawer()

    await drawer.get('button.switch').trigger('click')
    expect(drawer.get('.title').text()).toBe('Get a pass')
    expect(drawer.get('input[type="password"]').attributes('autocomplete')).toBe('new-password')

    await fillAndSubmit(drawer)

    expect(registerAccount).toHaveBeenCalledTimes(1)
    expect(signIn).not.toHaveBeenCalled()
  })

  it.each([
    [401, "That email and password don't match."],
    [409, 'That email already has an account.'],
    [400, 'Use a valid email and a password of at least 8 characters.'],
    [429, 'Too many tries. Wait a minute and try again.'],
    [503, "Couldn't reach the store. Try again."],
  ])('explains a %i', async (status, message) => {
    vi.mocked(signIn).mockRejectedValueOnce(new ApiError(status, 'nope'))
    const drawer = mountDrawer()

    await fillAndSubmit(drawer)

    expect(drawer.get('[role="alert"]').text()).toBe(message)
    expect(useAccount().account.value).toBeNull()
  })

  it('clears the password after signing in', async () => {
    const drawer = mountDrawer()
    await fillAndSubmit(drawer)

    await drawer.get('.pass-foot button.switch').trigger('click')
    await flushPromises()

    expect((drawer.get('input[type="password"]').element as HTMLInputElement).value).toBe('')
  })
})

describe('AccountDrawer signed in', () => {
  beforeEach(() => {
    useAccount().account.value = { email: 'me@example.com' }
  })

  it('shows receipts with their lines and totals', () => {
    useAccount().orders.value = [order]

    const drawer = mountDrawer()

    expect(drawer.get('.receipt-head').text()).toContain('No. ABC123')
    expect(drawer.get('.line-meta').text()).toBe('Panda · US 10 · x2')
    expect(drawer.get('.total').text()).toContain('$230')
  })

  it('says when there are no receipts', () => {
    expect(mountDrawer().get('.empty').text()).toBe('No receipts yet.')
  })

  it('adds a saved shoe to the bag by size', async () => {
    const sneaker = makeSneaker({ sizes: [9, 10] })
    useAccount().wishlist.value = [sneaker]
    const drawer = mountDrawer()

    await drawer.findAll('button.tab')[1]!.trigger('click')
    await drawer.findAll('button.size')[1]!.trigger('click')
    await flushPromises()

    expect(useCart().lines[0]).toMatchObject({ size: 10, quantity: 1 })
    expect(drawer.findAll('button.size')[1]!.text()).toBe('✓')
  })

  it('signs out back to the form', async () => {
    const drawer = mountDrawer()

    await drawer.get('.pass-foot button.switch').trigger('click')
    await flushPromises()

    expect(useAccount().account.value).toBeNull()
    expect(drawer.find('form').exists()).toBe(true)
  })
})
