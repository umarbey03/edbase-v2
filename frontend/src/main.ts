import { VueQueryPlugin } from '@tanstack/vue-query'
import { createPinia } from 'pinia'
import { createApp } from 'vue'

import App from './App.vue'
import { queryClient, registerSessionExpiryRedirect } from './app/providers'
import { setupSentry } from './app/providers/sentry'
import { router } from './app/router'
import './style.css'

const app = createApp(App)

// Tartib muhim: pinia router guard'idan oldin o'rnatilishi kerak.
app.use(createPinia())
app.use(VueQueryPlugin, { queryClient })
app.use(router)

// Zaxira xato ishlovchisi DARHOL o'rnatiladi — Sentry yuklanayotgan
// (yoki umuman o'chiq) paytda ham xatolar yo'qolmasin.
app.config.errorHandler = (err, _instance, info) => {
  console.error('[vue] ishlov berilmagan xato:', info, err)
}

window.addEventListener('unhandledrejection', (event) => {
  console.error('[promise] ushlanmagan rad etish:', event.reason)
})

registerSessionExpiryRedirect(router)

// Mount'ni KUTMAYMIZ: Sentry fon rejimida yuklanadi va ilova darhol
// ko'rinadi. Sentry'ni kutish mobil internetda oq ekranni uzaytirardi.
// DSN bo'lmasa hech narsa yuklanmaydi (0 bayt).
void setupSentry(app, router).catch((e) => {
  console.warn('[sentry] ishga tushmadi (ilova normal ishlaydi):', e)
})

app.mount('#app')
