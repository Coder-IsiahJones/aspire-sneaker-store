import { useState, type FormEvent } from 'react'
import { startDropRush, type QueueDepth } from './api.ts'
import { useEventStream, type OrderEvent, type OrderEventKey } from './useEventStream.ts'
import { useQueues } from './useQueues.ts'

const said: Record<OrderEventKey, string> = {
  'order.placed': 'Order placed',
  'order.confirmed': 'Receipt sent',
  'order.packed': 'Packed',
}

// The store shows the same six characters on a receipt.
const orderNumber = (id: string) => id.slice(-6).toUpperCase()

const clock = new Intl.DateTimeFormat('en-US', { hour12: false, timeStyle: 'medium' })

function Bin({ queue }: { queue: QueueDepth }) {
  const deadLetters = queue.name === 'dead-letters'
  const hands = deadLetters
    ? 'Nobody collects these'
    : `${queue.workers} ${queue.workers === 1 ? 'worker' : 'workers'} on it`

  return (
    <article className={deadLetters && queue.waiting > 0 ? 'bin alarm' : 'bin'}>
      <h3>{queue.name}</h3>
      {/* The key restarts the stamp animation whenever the number changes. */}
      <p className="count" key={queue.waiting}>
        {queue.waiting}
      </p>
      <p className="unit">waiting</p>
      <p className="hands">{hands}</p>
    </article>
  )
}

function FeedLine({ event }: { event: OrderEvent }) {
  return (
    <li className={`line ${event.key.replace('order.', '')}`}>
      <time>{clock.format(event.receivedAt)}</time>
      <span className="key">{event.key}</span>
      <span className="what">
        {said[event.key]} · No. {orderNumber(event.orderId)}
      </span>
      {event.email && <span className="who">{event.email}</span>}
    </li>
  )
}

export function App() {
  const { live, events, tally } = useEventStream('/api/ops/events')
  const { queues, failed } = useQueues()
  const [orders, setOrders] = useState(25)
  const [doomed, setDoomed] = useState(true)
  const [busy, setBusy] = useState(false)
  const [note, setNote] = useState<string>()

  async function rush(submitted: FormEvent) {
    submitted.preventDefault()
    setBusy(true)
    setNote(undefined)
    try {
      const result = await startDropRush(orders, doomed)
      setNote(
        result.doomed > 0
          ? `Published ${result.published} orders. One of them can never get a receipt.`
          : `Published ${result.published} orders.`,
      )
    } catch {
      setNote("Couldn't reach the API.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="board">
      <header className="masthead">
        <div>
          <p className="eyebrow">Stockroom</p>
          <h1>Back room</h1>
        </div>
        <p className={live ? 'lamp on' : 'lamp'} role="status">
          {live ? 'Listening to the broker' : 'Not connected'}
        </p>
      </header>

      <section aria-labelledby="queues-title">
        <h2 id="queues-title">Queues</h2>
        {queues ? (
          <div className="bins">
            {queues.map((queue) => (
              <Bin key={queue.name} queue={queue} />
            ))}
          </div>
        ) : (
          <p className="quiet">{failed ? "Couldn't read the queues." : 'Counting…'}</p>
        )}
        <p className="quiet">
          Waiting means no worker has it yet. An order that a worker is busy with isn't counted.
        </p>
      </section>

      <section aria-labelledby="rush-title" className="dock">
        <h2 id="rush-title">Open a drop</h2>
        <form onSubmit={rush}>
          <label>
            Orders
            <input
              type="number"
              min={1}
              max={200}
              value={orders}
              onChange={(changed) => setOrders(Number(changed.target.value))}
            />
          </label>
          <label className="check">
            <input type="checkbox" checked={doomed} onChange={(changed) => setDoomed(changed.target.checked)} />
            Include one order that can never get its receipt
          </label>
          <button type="submit" disabled={busy}>
            {busy ? 'Publishing…' : 'Start the rush'}
          </button>
        </form>
        <p className="quiet" role="status">
          {note}
        </p>
      </section>

      <section aria-labelledby="feed-title">
        <h2 id="feed-title">Off the wire</h2>
        <p className="tally">
          <span>{tally['order.placed']} placed</span>
          <span>{tally['order.confirmed']} receipts sent</span>
          <span>{tally['order.packed']} packed</span>
        </p>
        {events.length === 0 ? (
          <p className="quiet">Nothing yet. Place an order in the store, or start a rush.</p>
        ) : (
          <ol className="feed">
            {events.map((event) => (
              <FeedLine key={event.seq} event={event} />
            ))}
          </ol>
        )}
      </section>
    </main>
  )
}
