import { beforeEach, describe, expect, it, vi } from 'vitest'
import { addToBag, getBag, removeFromBag, type BagLine } from '../api'
import { resetFakeBag } from '../test/fakeBag'
import { makeSneaker } from '../test/sneaker'
import { formatPrice, useCart } from './useCart'
import { getEntrantId } from './useDrops'

vi.mock('../api', async () => (await import('../test/fakeBag')).fakeBagApi)

// The cart is module-level state, so empty it between tests.
beforeEach(() => resetFakeBag(useCart()))

describe('useCart', () => {
  it('starts empty', () => {
    const { lines, count, total } = useCart()

    expect(lines).toHaveLength(0)
    expect(count.value).toBe(0)
    expect(total.value).toBe(0)
  })

  it('loads the saved bag for this browser', async () => {
    const sneaker = makeSneaker()
    const saved = [{ key: `${sneaker.id}:9`, sneaker, size: 9, quantity: 2 }]
    vi.mocked(getBag).mockResolvedValueOnce(saved)
    const { lines, count, load } = useCart()

    await expect(load()).resolves.toBe(true)

    expect(getBag).toHaveBeenLastCalledWith(getEntrantId())
    expect(lines).toEqual(saved)
    expect(count.value).toBe(2)
  })

  it('adds through the API and shows the returned bag', async () => {
    const { lines, add } = useCart()
    const sneaker = makeSneaker()

    await expect(add(sneaker, 9.5)).resolves.toBe(true)

    expect(addToBag).toHaveBeenCalledWith(getEntrantId(), sneaker.id, 9.5)
    expect(lines).toEqual([{ key: `${sneaker.id}:9.5`, sneaker, size: 9.5, quantity: 1 }])
  })

  it('bumps the quantity when the same sneaker and size is added again', async () => {
    const { lines, count, add } = useCart()
    const sneaker = makeSneaker()

    await add(sneaker, 10)
    await add(sneaker, 10)

    expect(lines).toHaveLength(1)
    expect(lines[0]!.quantity).toBe(2)
    expect(count.value).toBe(2)
  })

  it('keeps different sizes of the same sneaker on separate lines', async () => {
    const { lines, add } = useCart()
    const sneaker = makeSneaker()

    await add(sneaker, 9)
    await add(sneaker, 10)

    expect(lines.map((line) => line.size)).toEqual([9, 10])
  })

  it('totals price times quantity across lines', async () => {
    const { count, total, add } = useCart()
    const cheap = makeSneaker({ price: 100 })
    const pricey = makeSneaker({ price: 210 })

    await add(cheap, 9)
    await add(cheap, 9)
    await add(pricey, 11)

    expect(count.value).toBe(3)
    expect(total.value).toBe(410)
  })

  it('removes a line by key', async () => {
    const { lines, count, add, remove } = useCart()
    const a = makeSneaker()
    const b = makeSneaker()
    await add(a, 9)
    await add(a, 9)
    await add(b, 10)

    await remove(`${a.id}:9`)

    expect(removeFromBag).toHaveBeenCalledWith(getEntrantId(), `${a.id}:9`)
    expect(lines.map((line) => line.sneaker)).toEqual([b])
    expect(count.value).toBe(1)
  })

  it('reports a failed add and leaves the bag as it was', async () => {
    const { lines, add } = useCart()
    await add(makeSneaker(), 9)
    vi.mocked(addToBag).mockRejectedValueOnce(new Error('503'))

    await expect(add(makeSneaker(), 10)).resolves.toBe(false)

    expect(lines).toHaveLength(1)
  })

  it('reports a failed remove and keeps the line', async () => {
    const sneaker = makeSneaker()
    const { lines, add, remove } = useCart()
    await add(sneaker, 9)
    vi.mocked(removeFromBag).mockRejectedValueOnce(new Error('503'))

    await expect(remove(`${sneaker.id}:9`)).resolves.toBe(false)

    expect(lines).toHaveLength(1)
  })

  it('ignores a slow response that a newer one has replaced', async () => {
    const { lines, add } = useCart()
    const slow = makeSneaker()
    const fast = makeSneaker()
    const line = (sneaker: typeof slow): BagLine => ({
      key: `${sneaker.id}:9`,
      sneaker,
      size: 9,
      quantity: 1,
    })
    let finishSlow!: (result: BagLine[]) => void
    vi.mocked(addToBag).mockReturnValueOnce(new Promise((resolve) => (finishSlow = resolve)))
    vi.mocked(addToBag).mockResolvedValueOnce([line(fast)])

    const first = add(slow, 9)
    await add(fast, 9)
    finishSlow([line(slow)])
    await first

    expect(lines.map((l) => l.sneaker)).toEqual([fast])
  })

  it('shares one cart between callers', async () => {
    await useCart().add(makeSneaker(), 9)

    expect(useCart().count.value).toBe(1)
  })
})

describe('formatPrice', () => {
  it('drops cents on whole dollar amounts', () => {
    expect(formatPrice(180)).toBe('$180')
  })

  it('keeps cents when there are some', () => {
    expect(formatPrice(179.99)).toBe('$179.99')
  })

  it('adds a thousands separator', () => {
    expect(formatPrice(1250)).toBe('$1,250')
  })
})
