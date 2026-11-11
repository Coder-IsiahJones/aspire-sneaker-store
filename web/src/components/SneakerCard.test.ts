import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { addToBag, type BagLine } from '../api'
import { useCart } from '../composables/useCart'
import { resetFakeBag } from '../test/fakeBag'
import { makeSneaker } from '../test/sneaker'
import SneakerCard from './SneakerCard.vue'

vi.mock('../api', async () => (await import('../test/fakeBag')).fakeBagApi)

beforeEach(() => resetFakeBag(useCart()))

afterEach(() => {
  vi.useRealTimers()
})

function mountCard(overrides = {}) {
  const sneaker = makeSneaker({
    sku: 'NKE-DNK-PND',
    name: 'Dunk Low',
    brand: 'Nike',
    colorway: 'Panda',
    price: 115,
    releaseYear: 2021,
    sizes: [8, 9.5, 11],
    ...overrides,
  })
  const wrapper = mount(SneakerCard, { props: { sneaker, index: 0 } })
  return { sneaker, wrapper }
}

const addButton = (wrapper: ReturnType<typeof mountCard>['wrapper']) => wrapper.get('button.add')
const sizeButton = (wrapper: ReturnType<typeof mountCard>['wrapper'], size: string) =>
  wrapper.findAll('button.size').find((button) => button.text() === size)!

describe('SneakerCard', () => {
  it('shows the box label details', () => {
    const { wrapper } = mountCard()

    expect(wrapper.get('.brand').text()).toBe('Nike')
    expect(wrapper.get('.sku').text()).toBe('NKE-DNK-PND')
    expect(wrapper.get('.name').text()).toBe('Dunk Low')
    expect(wrapper.get('.colorway').text()).toBe('"Panda"')
    expect(wrapper.get('.year').text()).toBe('2021')
    expect(wrapper.get('.price').text()).toBe('$115')
  })

  it('offers one button per size in stock', () => {
    const { wrapper } = mountCard()

    expect(wrapper.findAll('button.size').map((button) => button.text())).toEqual(['8', '9.5', '11'])
  })

  it('will not add to the bag until a size is picked', async () => {
    const { wrapper } = mountCard()

    expect(addButton(wrapper).text()).toBe('Pick a size')
    expect(addButton(wrapper).attributes('disabled')).toBeDefined()

    await addButton(wrapper).trigger('click')

    expect(useCart().count.value).toBe(0)
  })

  it('marks the picked size and names it on the add button', async () => {
    const { wrapper } = mountCard()

    await sizeButton(wrapper, '9.5').trigger('click')

    expect(sizeButton(wrapper, '9.5').attributes('aria-pressed')).toBe('true')
    expect(sizeButton(wrapper, '8').attributes('aria-pressed')).not.toBe('true')
    expect(addButton(wrapper).text()).toBe('Add size 9.5')
    expect(addButton(wrapper).attributes('disabled')).toBeUndefined()
  })

  it('adds the picked size to the bag and briefly confirms it', async () => {
    vi.useFakeTimers()
    const { sneaker, wrapper } = mountCard()

    await sizeButton(wrapper, '11').trigger('click')
    await addButton(wrapper).trigger('click')
    await flushPromises()

    expect(useCart().lines).toEqual([
      { key: `${sneaker.id}:11`, sneaker, size: 11, quantity: 1 },
    ])
    expect(addButton(wrapper).text()).toBe('In the bag')

    await vi.advanceTimersByTimeAsync(1200)

    expect(addButton(wrapper).text()).toBe('Add size 11')
  })

  it('says so when the add fails, and clears it on a new size', async () => {
    const { wrapper } = mountCard()
    vi.mocked(addToBag).mockRejectedValueOnce(new Error('503'))

    await sizeButton(wrapper, '11').trigger('click')
    await addButton(wrapper).trigger('click')
    await flushPromises()

    expect(addButton(wrapper).text()).toBe("Didn't save. Try again")
    expect(useCart().count.value).toBe(0)

    await sizeButton(wrapper, '8').trigger('click')

    expect(addButton(wrapper).text()).toBe('Add size 8')
  })

  it('blocks a second click while the add is in flight', async () => {
    const { wrapper } = mountCard()
    let finish!: (lines: BagLine[]) => void
    vi.mocked(addToBag).mockReturnValueOnce(new Promise((resolve) => (finish = resolve)))
    await sizeButton(wrapper, '11').trigger('click')

    await addButton(wrapper).trigger('click')

    expect(addButton(wrapper).attributes('disabled')).toBeDefined()
    await addButton(wrapper).trigger('click')
    expect(addToBag).toHaveBeenCalledTimes(1)

    finish([])
    await flushPromises()
    expect(addButton(wrapper).attributes('disabled')).toBeUndefined()
  })

  it('draws a barcode that depends on the SKU', () => {
    const bars = (sku: string) =>
      mountCard({ sku })
        .wrapper.findAll('.barcode rect')
        .map((rect) => `${rect.attributes('x')}/${rect.attributes('width')}`)

    // Three bars per SKU character.
    expect(bars('NKE-DNK-PND')).toHaveLength(33)
    expect(bars('NKE-DNK-PND')).toEqual(bars('NKE-DNK-PND'))
    expect(bars('NKE-DNK-PND')).not.toEqual(bars('ADI-SMB-OGX'))
  })
})
