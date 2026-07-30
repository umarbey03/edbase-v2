/**
 * Brauzerda fayl saqlash oynasini ochish.
 *
 * NEGA ALOHIDA FUNKSIYA: `URL.createObjectURL` bilan yaratilgan manzil
 * xotirada QOLADI va sahifa yopilgunicha bo'shatilmaydi. Bir necha marta
 * eksport qilingan hisobot brauzerda o'nlab megabayt ushlab turardi.
 * Shu sababli `revoke` shu yerda, bitta joyda kafolatlanadi.
 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.rel = 'noopener'

  // Element DOM'ga QO'SHILADI: Firefox biriktirilmagan havolaning
  // `click()` ini e'tiborsiz qoldiradi va yuklash umuman boshlanmasdi.
  document.body.appendChild(link)
  link.click()
  link.remove()

  // Yuklash boshlanishiga ulgurish uchun kichik kechikish — manzil darhol
  // bekor qilinsa Safari faylni "tarmoq xatosi" deb rad etadi.
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}
