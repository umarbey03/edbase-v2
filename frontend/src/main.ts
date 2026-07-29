import { VueQueryPlugin } from '@tanstack/vue-query'
import { createPinia } from 'pinia'
import { createApp } from 'vue'

import App from './App.vue'
import { queryClient, registerSessionExpiryRedirect } from './app/providers'
import { router } from './app/router'
import './style.css'

const app = createApp(App)

// Tartib muhim: pinia router guard'idan oldin o'rnatilishi kerak.
app.use(createPinia())
app.use(VueQueryPlugin, { queryClient })
app.use(router)

registerSessionExpiryRedirect(router)

app.mount('#app')
