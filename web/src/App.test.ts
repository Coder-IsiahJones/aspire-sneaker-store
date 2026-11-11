import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App.vue'
import { getBrands, getSneakers } from './api'
import { useCart } from './composables/useCart'
import { makeSneaker } from './test/sneaker'

vi.mock('./api', () => ({
  getSneakers: vi.fn(),
  getBrands: vi.fn(),
}))

const dunk = makeSneaker({ name: 'Dunk Low', brand: 'Nike' })
const samba = makeSneaker({ name: 'Samba OG', brand: 'adidas' })

beforeEach(() => {
  useCart().lines.splice(0)
  vi.mocked(getSneakers).mockResolvedValue([samba, dunk])
  vi.mocked(getBrands).mockResolvedValue(['adidas', 'Nike'])
})

const cardNames = (wrapper: ReturnType<typeof mount>) =>
  wrapper.findAll('article .name').map((name) => name.text())

describe('App', () => {
  it('shows a loading message until the catalog arrives', async () => {
    const wrapper = mount(App)

    expect(wrapper.get('.status').text()).toContain('Pulling boxes')

    await flushPromises()

    expect(wrapper.find('.status').exists()).toBe(false)
    expect(cardNames(wrapper)).toEqual(['Samba OG', 'Dunk Low'])
  })

  it('loads the brand tabs', async () => {
    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.findAll('nav button').map((button) => button.text())).toEqual([
      'All pairs',
      'adidas',
      'Nike',
    ])
  })

  it('reloads the catalog for a brand when its tab is clicked', async () => {
    const wrapper = mount(App)
    await flushPromises()
    vi.mocked(getSneakers).mockResolvedValue([dunk])

    await wrapper.findAll('nav button')[2]!.trigger('click')
    await flushPromises()

    expect(getSneakers).toHaveBeenLastCalledWith('Nike')
    expect(cardNames(wrapper)).toEqual(['Dunk Low'])
  })

  it('shows the error when the catalog cannot be loaded', async () => {
    vi.mocked(getSneakers).mockRejectedValue(new Error('503 Service Unavailable from /api/sneakers'))

    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.get('.status-error').text()).toContain("Couldn't reach the API.")
    expect(wrapper.get('.status-error code').text()).toBe('503 Service Unavailable from /api/sneakers')
  })

  it('still shows the catalog when the brand list fails', async () => {
    vi.mocked(getBrands).mockRejectedValue(new Error('boom'))

    const wrapper = mount(App)
    await flushPromises()

    expect(cardNames(wrapper)).toEqual(['Samba OG', 'Dunk Low'])
    expect(wrapper.findAll('nav button').map((button) => button.text())).toEqual(['All pairs'])
  })

  it('counts pairs in the bag and opens the cart', async () => {
    const wrapper = mount(App)
    await flushPromises()

    const card = wrapper.findAll('article')[1]!
    await card.findAll('button.size')[0]!.trigger('click')
    await card.get('button.add').trigger('click')

    expect(wrapper.get('.cart-count').text()).toBe('1')
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)

    await wrapper.get('button.cart-tag').trigger('click')

    expect(wrapper.get('[role="dialog"] .line-name').text()).toBe('Dunk Low')
    wrapper.unmount()
  })
})
