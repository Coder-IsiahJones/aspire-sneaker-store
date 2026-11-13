/** One row of GET /api/ops/queues. */
export interface QueueDepth {
  name: string
  /** Messages no worker holds yet. An order a worker is busy with isn't counted. */
  waiting: number
  workers: number
}

export interface RushResult {
  published: number
  doomed: number
}

async function readJson<T>(response: Response, path: string): Promise<T> {
  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText} from ${path}`)
  }
  return response.json() as Promise<T>
}

// Relative URLs: the Vite dev server proxies /api to the C# API.
export async function getQueues() {
  const path = '/api/ops/queues'
  return readJson<QueueDepth[]>(await fetch(path), path)
}

/** Publishes made-up orders, as if a drop just opened. */
export async function startDropRush(orders: number, doomed: boolean) {
  const path = `/api/ops/drop-rush?orders=${orders}&doomed=${doomed}`
  return readJson<RushResult>(await fetch(path, { method: 'POST' }), path)
}
