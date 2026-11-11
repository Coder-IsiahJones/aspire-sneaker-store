import { onBeforeUnmount, onMounted, ref } from 'vue'
import { enterDrop, getDrops, type Drop } from '../api'

const ENTRANT_KEY = 'stockroom.entrant'
const POLL_MS = 30_000
// Refetch just after a timer hits zero, so the server has definitely changed phase.
const BOUNDARY_GRACE_MS = 300

let sessionEntrant: string | undefined

function newEntrantId() {
  // getRandomValues, because randomUUID only exists on HTTPS and localhost.
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
}

/** A random id that stands in for an account, so a browser can only enter a drop once. */
export function getEntrantId(): string {
  try {
    const stored = localStorage.getItem(ENTRANT_KEY)
    if (stored) return stored
    const created = sessionEntrant ?? newEntrantId()
    localStorage.setItem(ENTRANT_KEY, created)
    return created
  } catch {
    // Storage can be blocked. Entries then last only as long as the page does.
    return (sessionEntrant ??= newEntrantId())
  }
}

export function useDrops() {
  const drops = ref<Drop[]>([])
  const now = ref(Date.now())
  const entering = ref<string>()
  const failed = ref<string>()

  let clockOffset = 0
  let latestRequest = 0
  let disposed = false
  let tick: ReturnType<typeof setInterval> | undefined
  let refresh: ReturnType<typeof setTimeout> | undefined

  const serverNow = () => Date.now() + clockOffset

  function scheduleRefresh() {
    clearTimeout(refresh)
    if (disposed) return

    const current = serverNow()
    const boundaries = drops.value
      .flatMap((drop) => [Date.parse(drop.opensAt), Date.parse(drop.closesAt)])
      .filter((time) => time > current)
    const untilBoundary = boundaries.length
      ? Math.min(...boundaries) - current + BOUNDARY_GRACE_MS
      : POLL_MS

    refresh = setTimeout(load, Math.max(1000, Math.min(POLL_MS, untilBoundary)))
  }

  async function load() {
    const request = ++latestRequest
    try {
      const result = await getDrops(getEntrantId())
      // A slow response must not overwrite a newer one.
      if (request !== latestRequest) return
      clockOffset = Date.parse(result.serverTime) - Date.now()
      now.value = serverNow()
      drops.value = result.drops
    } catch {
      // Keep whatever is on screen. The next refresh tries again.
    } finally {
      if (request === latestRequest) scheduleRefresh()
    }
  }

  async function enter(dropId: string, size: number) {
    if (entering.value) return
    entering.value = dropId
    failed.value = undefined
    try {
      await enterDrop(dropId, getEntrantId(), size)
    } catch {
      failed.value = dropId
    } finally {
      // Also runs on failure: a 409 usually means this card is out of date.
      await load()
      entering.value = undefined
    }
  }

  onMounted(() => {
    tick = setInterval(() => (now.value = serverNow()), 1000)
    void load()
  })

  onBeforeUnmount(() => {
    disposed = true
    clearInterval(tick)
    clearTimeout(refresh)
  })

  return { drops, now, entering, failed, enter }
}
