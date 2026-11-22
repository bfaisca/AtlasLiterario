import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { createAuthGuard } from './guards'
import { routes } from './routes'

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
  scrollBehavior(_to, _from, saved) {
    return saved ?? { top: 0 }
  },
})

router.beforeEach(createAuthGuard(() => useAuthStore()))

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · Atlas Literário` : 'Atlas Literário'
})
