import { useEffect, useReducer } from 'react'

/** The routing keys the API relays. Each one arrives as a named Server-Sent Event. */
export const ORDER_EVENTS = ['order.placed', 'order.confirmed', 'order.packed'] as const
export type OrderEventKey = (typeof ORDER_EVENTS)[number]

export interface OrderEvent {
  /** Counts up from 1, so every line in the feed has a stable React key. */
  seq: number
  key: OrderEventKey
  orderId: string
  /** Only order.placed carries the shopper's email. */
  email?: string
  receivedAt: Date
}

export type Tally = Record<OrderEventKey, number>

interface Stream {
  live: boolean
  events: OrderEvent[]
  tally: Tally
  seq: number
}

type Action =
  | { type: 'live'; live: boolean }
  | { type: 'event'; key: OrderEventKey; orderId: string; email?: string; keep: number }

const start: Stream = {
  live: false,
  events: [],
  tally: { 'order.placed': 0, 'order.confirmed': 0, 'order.packed': 0 },
  seq: 0,
}

function reduce(stream: Stream, action: Action): Stream {
  if (action.type === 'live') {
    return { ...stream, live: action.live }
  }

  const seq = stream.seq + 1
  const next: OrderEvent = {
    seq,
    key: action.key,
    orderId: action.orderId,
    email: action.email,
    receivedAt: new Date(),
  }
  return {
    live: true,
    seq,
    // Newest first, and only the most recent lines. The tally still counts everything.
    events: [next, ...stream.events].slice(0, action.keep),
    tally: { ...stream.tally, [action.key]: stream.tally[action.key] + 1 },
  }
}

/**
 * Listens to the API's order events for as long as the component is mounted.
 * EventSource reconnects by itself, so there is no retry code here.
 */
export function useEventStream(url: string, keep = 200) {
  const [stream, dispatch] = useReducer(reduce, start)

  useEffect(() => {
    const source = new EventSource(url)

    // The API sends 'ready' first on every connection.
    source.addEventListener('ready', () => dispatch({ type: 'live', live: true }))
    source.addEventListener('error', () => dispatch({ type: 'live', live: false }))

    // Named events don't reach onmessage, so each key gets its own listener.
    for (const key of ORDER_EVENTS) {
      source.addEventListener(key, (event) => {
        const { orderId, email } = JSON.parse(event.data) as { orderId: string; email?: string }
        dispatch({ type: 'event', key, orderId, email, keep })
      })
    }

    return () => source.close()
  }, [url, keep])

  return { live: stream.live, events: stream.events, tally: stream.tally }
}
