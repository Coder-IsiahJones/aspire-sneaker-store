import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App.tsx'
import { getQueues, startDropRush } from './api.ts'
import { FakeEventSource } from './test/fakeEventSource.ts'

vi.mock('./api.ts', () => ({ getQueues: vi.fn(), startDropRush: vi.fn() }))

beforeEach(() => {
  FakeEventSource.opened = []
  vi.stubGlobal('EventSource', FakeEventSource)
  vi.mocked(getQueues).mockResolvedValue([
    { name: 'receipts', waiting: 0, workers: 2 },
    { name: 'warehouse', waiting: 12, workers: 2 },
    { name: 'dead-letters', waiting: 1, workers: 0 },
  ])
  vi.mocked(startDropRush).mockResolvedValue({ published: 25, doomed: 1 })
})

afterEach(cleanup)

const bin = async (name: string) => (await screen.findByRole('heading', { name })).closest('article')!

describe('App', () => {
  it('shows how many orders wait in each queue and who is working on it', async () => {
    render(<App />)

    const warehouse = within(await bin('warehouse'))
    expect(warehouse.getByText('12')).toBeTruthy()
    expect(warehouse.getByText('2 workers on it')).toBeTruthy()
  })

  it('raises the alarm when something lands in the dead letters', async () => {
    render(<App />)

    const deadLetters = await bin('dead-letters')
    expect(deadLetters.className).toContain('alarm')
    expect(within(deadLetters).getByText('Nobody collects these')).toBeTruthy()
    expect((await bin('receipts')).className).not.toContain('alarm')
  })

  it('starts a rush with what the form says, and reports the result', async () => {
    render(<App />)

    fireEvent.change(screen.getByLabelText('Orders'), { target: { value: '40' } })
    fireEvent.click(screen.getByRole('button', { name: 'Start the rush' }))

    await waitFor(() => expect(startDropRush).toHaveBeenCalledWith(40, true))
    expect(await screen.findByText('Published 25 orders. One of them can never get a receipt.')).toBeTruthy()
  })

  it('prints events as they arrive, with the order number the store shows', async () => {
    render(<App />)
    expect(screen.getByText('Not connected')).toBeTruthy()

    act(() => {
      FakeEventSource.opened.at(-1)!.emit('ready')
      FakeEventSource.opened.at(-1)!.emit('order.placed', {
        orderId: '6af67570f130610dbf1cfdea',
        email: 'me@example.com',
      })
    })

    expect(screen.getByText('Listening to the broker')).toBeTruthy()
    expect(screen.getByText('Order placed · No. 1CFDEA')).toBeTruthy()
    expect(screen.getByText('me@example.com')).toBeTruthy()
    expect(screen.getByText('1 placed')).toBeTruthy()
  })
})
