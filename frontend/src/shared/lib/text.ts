/** Ism-familiyadan avatar uchun bosh harflar: "Alisher Navoiy" -> "AN". */
export function initials(fullName: string): string {
  const parts = fullName
    .trim()
    .split(/\s+/)
    .filter((part) => part.length > 0)

  const first = parts[0]?.[0] ?? ''
  const second = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : ''
  const result = `${first}${second}`.toUpperCase()
  return result.length > 0 ? result : '?'
}

/**
 * Ismdan barqaror rang indeksi (bir xil ism — doim bir xil rang).
 * `Math.random` ishlatilmaydi, aks holda har render'da rang o'zgarib ketadi.
 */
export function colorIndex(seed: string, buckets: number): number {
  let hash = 0
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash << 5) - hash + seed.charCodeAt(i)
    hash |= 0
  }
  return Math.abs(hash) % buckets
}

export function truncate(value: string, max: number): string {
  return value.length <= max ? value : `${value.slice(0, max - 1)}…`
}
