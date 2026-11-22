import { createPinia } from 'pinia'
import { createApp } from 'vue'
import { tokenManager } from '@/api/tokenManager'
import App from './App.vue'
import { router } from './router'
import { useAuthStore } from './stores/auth'
import { useUiStore } from './stores/ui'
import './styles/main.css'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(router)

// Refresh falhou durante o uso: encerra a sessão e volta ao login preservando o destino.
tokenManager.onSessionExpired(() => {
  useAuthStore(pinia).clearSession()
  useUiStore(pinia).info('Sua sessão expirou. Entre novamente.')
  const current = router.currentRoute.value
  if (current.meta.requiresAuth) {
    void router.replace({ name: 'login', query: { redirect: current.fullPath } })
  }
})

app.mount('#app')
