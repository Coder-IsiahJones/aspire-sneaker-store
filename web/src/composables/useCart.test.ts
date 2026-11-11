import { beforeEach, describe, expect, it } from 'vitest'
import { makeSneaker } from '../test/sneaker'
import { formatPrice, useCart } from './useCart'

// The cart is module-level state, so empty it between tests.
beforeEach(() => {
  useCart().lines.splice(0)
})

describe('useCart', () => {
  it('starts empty', () => {
    const { lines, count, total } = useCart()

    expect(lines).toHaveLength(0)
    expect(count.value).toBe(0)
    expect(total.value).toBe(0)
  })

  it('adds a line keyed by sneaker id and size', () => {
    const { lines, add } = useCart()
    const sneaker = makeSneaker()

    add(sneaker, 9.5)

    expect(lines).toEqual([{ key: `${sneaker.id}:9.5`, sneaker, size: 9.5, quantity: 1 }])
  })

  it('bumps the quantity when the same sneaker and size is added again', () => {
    const { lines, count, add } = useCart()
    const sneaker = makeSneaker()

    add(sneaker, 10)
    add(sneaker, 10)

    expect(lines).toHaveLength(1)
    expect(lines[0]!.quantity).toBe(2)
    expect(count.value).toBe(2)
  })

  it('keeps different sizes of the same sneaker on separate lines', () => {
    const { lines, add } = useCart()
    const sneaker = makeSneaker()

    add(sneaker, 9)
    add(sneaker, 10)

    expect(lines.map((line) => line.size)).toEqual([9, 10])
  })

  it('totals price times quantity across lines', () => {
    const { count, total, add } = useCart()
    const cheap = makeSneaker({ price: 100 })
    const pricey = makeSneaker({ price: 210 })

    add(cheap, 9)
    add(cheap, 9)
    add(pricey, 11)

    expect(count.value).toBe(3)
    expect(total.value).toBe(410)
  })

  it('removes a line by key', () => {
    const { lines, count, add, remove } = useCart()
    const a = makeSneaker()
    const b = makeSneaker()
    add(a, 9)
    add(a, 9)
    add(b, 10)

    remove(`${a.id}:9`)

    expect(lines.map((line) => line.sneaker)).toEqual([b])
    expect(count.value).toBe(1)
  })

  it('ignores a remove for a key that is not in the cart', () => {
    const { lines, add, remove } = useCart()
    add(makeSneaker(), 9)

    remove('missing:9')

    expect(lines).toHaveLength(1)
  })

  it('shares one cart between callers', () => {
    useCart().add(makeSneaker(), 9)

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
