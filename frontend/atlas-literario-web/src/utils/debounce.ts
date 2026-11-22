export interface Debounced<A extends unknown[]> {
  (...args: A): void
  cancel(): void
  flush(): void
}

/** Atrasa a execução até `ms` sem novas chamadas. */
export function debounce<A extends unknown[]>(fn: (...args: A) => void, ms: number): Debounced<A> {
  let timer: ReturnType<typeof setTimeout> | null = null
  let lastArgs: A | null = null
  const debounced = ((...args: A) => {
    lastArgs = args
    if (timer) clearTimeout(timer)
    timer = setTimeout(() => {
      timer = null
      fn(...args)
    }, ms)
  }) as Debounced<A>
  debounced.cancel = () => {
    if (timer) clearTimeout(timer)
    timer = null
  }
  debounced.flush = () => {
    if (timer && lastArgs) {
      clearTimeout(timer)
      timer = null
      fn(...lastArgs)
    }
  }
  return debounced
}
