/** Stands in for the browser's EventSource, so a test can push events through it. */
export class FakeEventSource {
  static opened: FakeEventSource[] = []

  readonly url: string
  closed = false
  private readonly listeners = new Map<string, ((event: MessageEvent) => void)[]>()

  constructor(url: string) {
    this.url = url
    FakeEventSource.opened.push(this)
  }

  addEventListener(type: string, listener: (event: MessageEvent) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener])
  }

  close() {
    this.closed = true
  }

  emit(type: string, data: unknown = null) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({ data: JSON.stringify(data) } as MessageEvent)
    }
  }
}
