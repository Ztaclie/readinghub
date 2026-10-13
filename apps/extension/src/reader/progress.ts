export function contentProgress(scrollTop: number, scrollHeight: number, clientHeight: number) {
  const max = scrollHeight - clientHeight
  if (max <= 1) return 100
  return Math.min(100, Math.max(0, (scrollTop / max) * 100))
}

export function scrollTopForPercent(scrollHeight: number, clientHeight: number, percent: number) {
  const max = Math.max(0, scrollHeight - clientHeight)
  const clamped = Math.min(100, Math.max(0, percent))
  return (clamped / 100) * max
}

export function createProgressTracker(options: {
  threshold: number
  autoComplete: boolean
  emit: (percent: number, complete: boolean) => void
  intervalMs?: number
}) {
  let previous: number | null = null
  let minSeen = 100
  let armed = false
  let lastEmitAt = Number.NEGATIVE_INFINITY
  const interval = options.intervalMs ?? 2000

  return {
    get armed() {
      return armed
    },
    sample(percent: number, at: number) {
      minSeen = Math.min(minSeen, percent)
      if (minSeen < options.threshold - 20) armed = true
      const complete = options.autoComplete && armed && percent >= options.threshold && previous !== null
      const moved = previous !== null && Math.abs(percent - previous) >= 5
      previous = percent
      if ((moved || complete) && at - lastEmitAt >= interval) {
        lastEmitAt = at
        options.emit(round(percent), complete)
        return true
      }
      return false
    },
  }
}

function round(value: number) {
  return Math.round(value * 10) / 10
}
