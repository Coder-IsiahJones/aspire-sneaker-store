import { useEffect, useState } from 'react'
import { getQueues, type QueueDepth } from './api.ts'

/** Asks the API how deep each queue is, once a second. */
export function useQueues(everyMs = 1000) {
  const [queues, setQueues] = useState<QueueDepth[]>()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let stopped = false

    async function read() {
      try {
        const next = await getQueues()
        if (!stopped) {
          setQueues(next)
          setFailed(false)
        }
      } catch {
        if (!stopped) {
          setFailed(true)
        }
      }
    }

    void read()
    const timer = setInterval(() => void read(), everyMs)
    return () => {
      stopped = true
      clearInterval(timer)
    }
  }, [everyMs])

  return { queues, failed }
}
