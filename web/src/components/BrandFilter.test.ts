import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import BrandFilter from './BrandFilter.vue'

function mountFilter(selected?: string) {
  return mount(BrandFilter, {
    props: {
      brands: ['adidas', 'Nike', 'Vans'],
      modelValue: selected,
      'onUpdate:modelValue': () => {},
    },
  })
}

const pressed = (wrapper: ReturnType<typeof mountFilter>) =>
  wrapper
    .findAll('button')
    .filter((button) => button.attributes('aria-pressed') === 'true')
    .map((button) => button.text())

describe('BrandFilter', () => {
  it('renders an "All pairs" tab followed by one tab per brand', () => {
    const wrapper = mountFilter()

    expect(wrapper.findAll('button').map((button) => button.text())).toEqual([
      'All pairs',
      'adidas',
      'Nike',
      'Vans',
    ])
  })

  it('marks "All pairs" as pressed when no brand is selected', () => {
    expect(pressed(mountFilter())).toEqual(['All pairs'])
  })

  it('marks the selected brand as pressed', () => {
    expect(pressed(mountFilter('Nike'))).toEqual(['Nike'])
  })

  it('selects a brand when its tab is clicked', async () => {
    const wrapper = mountFilter()

    await wrapper.findAll('button')[2]!.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([['Nike']])
  })

  it('clears the selection when "All pairs" is clicked', async () => {
    const wrapper = mountFilter('Nike')

    await wrapper.findAll('button')[0]!.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[undefined]])
  })
})
