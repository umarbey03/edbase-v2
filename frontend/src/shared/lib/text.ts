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

/**
 * Fayl hajmi: `812 KB`, `3.4 MB`.
 *
 * 1024 lik bo'luvchi ishlatiladi, chunki server chegaralari ham shunday
 * (`5 * 1024 * 1024`). 1000 lik bo'lsa "4.9 MB" deb ko'rsatilgan fayl
 * serverda chegaradan oshib ketardi va foydalanuvchi buni tushunmasdi.
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const kilobytes = bytes / 1024
  if (kilobytes < 1024) return `${Math.round(kilobytes)} KB`
  return `${(kilobytes / 1024).toFixed(1)} MB`
}
