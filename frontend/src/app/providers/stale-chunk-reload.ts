/**
 * ESKI CHUNK — yangi build'dan keyin ochiq qolgan tab.
 *
 * Vite har build'da chunk nomlariga yangi hash beradi (`AppShell-abc123.js`).
 * Ilova ochiq turganda yangi build chiqsa, xotiradagi eski `index.js` hali
 * ESKI nomlarni biladi va lazy-route ochilganda serverdan 404 oladi:
 *   "Failed to fetch dynamically imported module: .../AppShell-xxxx.js"
 *
 * Vite bunday holatda `vite:preloadError` hodisasini chiqaradi. Bitta
 * to'g'ri javob — sahifani qayta yuklash: `index.html` `no-cache` bilan
 * beriladi (nginx.conf), demak yangi hash'lar keladi.
 *
 * Cheksiz aylanishdan himoya: oxirgi qayta yuklash vaqti saqlanadi va
 * 30 soniya ichida ikkinchi marta qayta yuklanmaydi. Ikkinchi yiqilish
 * hash'dan emas (server yotgan, tarmoq yo'q) — o'shanda hodisa odatdagidek
 * yuqoriga ketadi va Sentry ko'radi.
 */
const KEY = 'zinnur:stale-chunk-reload-at'
const COOLDOWN_MS = 30_000

export function registerStaleChunkReload(): void {
  window.addEventListener('vite:preloadError', (event) => {
    const now = Date.now()
    let last = 0
    try {
      last = Number(sessionStorage.getItem(KEY) ?? 0)
    } catch {
      // sessionStorage yopiq (private rejim) — baribir bir marta urinamiz.
    }
    if (now - last < COOLDOWN_MS) return

    try {
      sessionStorage.setItem(KEY, String(now))
    } catch {
      // ahamiyatsiz
    }
    event.preventDefault()
    console.warn('[chunk] eski build chunk topilmadi — sahifa qayta yuklanadi')
    window.location.reload()
  })
}
