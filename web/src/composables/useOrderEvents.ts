import { onScopeDispose, watch } from 'vue'
import type { OrderUpdate } from '../api'
import { useAccount } from './useAccount'

/**
 * Keeps the signed-in shopper's orders current while the page is open. The API pushes a small
 * event when the worker finishes a job, over Server-Sent Events: one long-lived GET that the
 * browser reconnects by itself. Call once, from the root component.
 */
export function useOrderEvents() {
  const { account, refreshOrders, markOrder } = useAccount()
  let source: EventSource | undefined

  function stop() {
    source?.close()
    source = undefined
  }

  function start() {
    // Relative URL, so the session cookie goes with it through the Vite proxy.
    source = new EventSource('/api/orders/events')

    // The API sends 'ready' first on every connection, reconnects included. Reading the orders
    // again here picks up anything that happened while the stream was down.
    source.addEventListener('ready', () => void refreshOrders().catch(() => {}))

    // Named events don't reach onmessage, so each one needs its own listener.
    source.addEventListener('order.confirmed', (event) => {
      const { orderId, at } = JSON.parse(event.data) as OrderUpdate
      void markOrder(orderId, 'receiptSentAt', at).catch(() => {})
    })
    source.addEventListener('order.packed', (event) => {
      const { orderId, at } = JSON.parse(event.data) as OrderUpdate
      void markOrder(orderId, 'packedAt', at).catch(() => {})
    })
  }

  // The stream needs a session, and EventSource gives up for good on a 401, so it is only open
  // while someone is signed in.
  watch(
    () => account.value?.email,
    (email) => {
      stop()
      if (email) {
        start()
      }
    },
    { immediate: true },
  )

  onScopeDispose(stop)
}
