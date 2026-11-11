const ENTRANT_KEY = 'stockroom.entrant'

let sessionEntrant: string | undefined

function newEntrantId() {
  // getRandomValues, because randomUUID only exists on HTTPS and localhost.
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
}

/** A random id for this browser, used until the shopper signs in to an account. */
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
