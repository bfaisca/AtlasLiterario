<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppIcon from '@/components/ui/AppIcon.vue'
import type { IconName } from '@/components/ui/icons'
import { USE_MOCKS } from '@/api'

interface NavItem {
  name: string
  label: string
  icon: IconName
  mobile: boolean
}

const NAV: NavItem[] = [
  { name: 'dashboard', label: 'Início', icon: 'home', mobile: true },
  { name: 'library', label: 'Biblioteca', icon: 'library', mobile: true },
  { name: 'search', label: 'Adicionar', icon: 'plus', mobile: true },
  { name: 'map', label: 'Mapa', icon: 'globe', mobile: true },
  { name: 'passports', label: 'Passaportes', icon: 'passport', mobile: true },
  { name: 'people', label: 'Pessoas', icon: 'users', mobile: false },
  { name: 'profile', label: 'Perfil', icon: 'user', mobile: false },
]

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

/** `library-item` mantém "Biblioteca" ativa. */
function isActive(name: string): boolean {
  const current = String(route.name ?? '')
  if (name === 'search' && current === 'book-new') return true
  // Perfil de outro leitor mantém "Pessoas" ativa; minhas listas de seguidores mantêm "Perfil".
  if (name === 'people' && current === 'user-profile') return true
  if (name === 'profile' && current.startsWith('me-')) return true
  return current === name || current.startsWith(`${name}-`)
}
const mobileNav = computed(() => NAV.filter((item) => item.mobile))
const initials = computed(() =>
  (auth.user?.name ?? '?')
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join(''),
)

async function logout() {
  await auth.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="app">
    <a href="#conteudo" class="skip-link">Pular para o conteúdo</a>

    <!-- Desktop: navegação lateral -->
    <aside class="sidebar" aria-label="Navegação principal">
      <RouterLink :to="{ name: 'dashboard' }" class="brand">
        <span class="eyebrow brand__eyebrow">Atlas</span>
        <span class="brand__wordmark">Literário</span>
      </RouterLink>

      <nav class="sidebar__nav">
        <RouterLink
          v-for="item in NAV"
          :key="item.name"
          :to="{ name: item.name }"
          class="nav-link"
          active-class=""
          exact-active-class=""
          :class="{ 'is-active': isActive(item.name) }"
        >
          <AppIcon :name="item.icon" />
          <span>{{ item.label }}</span>
        </RouterLink>
      </nav>

      <div class="sidebar__footer">
        <RouterLink :to="{ name: 'profile' }" class="user-chip">
          <span class="user-chip__avatar">{{ initials }}</span>
          <span class="user-chip__name">{{ auth.user?.name }}</span>
        </RouterLink>
        <button type="button" class="nav-link nav-link--button" @click="logout">
          <AppIcon name="logout" />
          <span>Sair</span>
        </button>
        <p class="attribution">
          Dados: Google Books, Open Library, Wikidata · Mapa: Natural Earth
          <template v-if="USE_MOCKS"><br />Modo demonstração (mocks)</template>
        </p>
      </div>
    </aside>

    <!-- Mobile: barra superior -->
    <header class="topbar">
      <RouterLink :to="{ name: 'dashboard' }" class="brand brand--compact">
        <span class="brand__wordmark">Atlas Literário</span>
      </RouterLink>
      <div class="topbar__actions">
        <RouterLink :to="{ name: 'people' }" class="topbar__icon" aria-label="Pessoas">
          <AppIcon name="users" :size="20" />
        </RouterLink>
        <RouterLink :to="{ name: 'profile' }" class="user-chip__avatar" aria-label="Perfil">
          {{ initials }}
        </RouterLink>
      </div>
    </header>

    <main id="conteudo" class="content" tabindex="-1">
      <RouterView v-slot="{ Component, route: viewRoute }">
        <Transition name="page-fade" mode="out-in">
          <component :is="Component" :key="viewRoute.name" />
        </Transition>
      </RouterView>
    </main>

    <!-- Mobile: barra inferior -->
    <nav class="bottom-nav" aria-label="Navegação principal">
      <RouterLink
        v-for="item in mobileNav"
        :key="item.name"
        :to="{ name: item.name }"
        class="bottom-nav__link"
        active-class=""
        exact-active-class=""
        :class="{ 'is-active': isActive(item.name) }"
      >
        <AppIcon :name="item.icon" :size="22" />
        <span>{{ item.label }}</span>
      </RouterLink>
    </nav>
  </div>
</template>

<style scoped>
.app {
  min-height: 100dvh;
}

.skip-link {
  position: absolute;
  left: var(--space-4);
  top: -100px;
  z-index: var(--z-toast);
  padding: var(--space-2) var(--space-4);
  background: var(--color-brass);
  color: var(--color-text-inverse);
  border-radius: var(--radius-md);
}
.skip-link:focus {
  top: var(--space-4);
}

.brand {
  display: flex;
  flex-direction: column;
  text-decoration: none;
  color: var(--color-text);
}
.brand:hover {
  color: var(--color-text);
}
.brand__eyebrow {
  color: var(--color-brass);
  letter-spacing: 0.4em;
}
.brand__wordmark {
  font-family: var(--font-display);
  font-style: italic;
  font-size: 1.9rem;
  line-height: 1;
}
.brand--compact .brand__wordmark {
  font-size: 1.35rem;
}

/* ---------- Sidebar (desktop) ---------- */
.sidebar {
  position: fixed;
  inset: 0 auto 0 0;
  width: var(--sidebar-width);
  z-index: var(--z-nav);
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
  padding: var(--space-6) var(--space-4) var(--space-5);
  border-right: 1px solid var(--color-hairline);
  background: linear-gradient(180deg, rgb(12 22 38 / 0.9), rgb(7 13 24 / 0.95));
}
.sidebar .brand {
  padding-inline: var(--space-3);
}
.sidebar__nav {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.nav-link {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--space-3);
  width: 100%;
  padding: 0.6rem var(--space-3);
  border-radius: var(--radius-md);
  color: var(--color-text-muted);
  text-decoration: none;
  font-size: var(--text-sm);
  font-weight: 500;
  transition:
    color var(--duration-fast) var(--ease-out),
    background-color var(--duration-fast) var(--ease-out);
}
.nav-link:hover {
  color: var(--color-text);
  background: var(--color-surface);
}
.nav-link.is-active {
  color: var(--color-brass-strong);
  background: var(--color-brass-soft);
}
.nav-link.is-active::before {
  content: '';
  position: absolute;
  left: -16px;
  top: 25%;
  bottom: 25%;
  width: 2px;
  border-radius: 2px;
  background: var(--color-brass);
}
.sidebar__footer {
  margin-top: auto;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.user-chip {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-md);
  text-decoration: none;
  color: var(--color-text);
}
.user-chip:hover {
  background: var(--color-surface);
  color: var(--color-text);
}
.user-chip__avatar {
  display: grid;
  place-items: center;
  width: 34px;
  height: 34px;
  border-radius: 50%;
  background: var(--color-surface-3);
  border: 1px solid var(--color-border);
  color: var(--color-brass-strong);
  font-size: var(--text-xs);
  font-weight: 700;
  letter-spacing: 0.05em;
  text-decoration: none;
  flex-shrink: 0;
}
.user-chip__name {
  font-size: var(--text-sm);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.attribution {
  padding: var(--space-3) var(--space-3) 0;
  font-size: 0.6875rem;
  line-height: 1.5;
  color: var(--color-text-faint);
}

.content {
  min-height: 100dvh;
  margin-left: var(--sidebar-width);
  outline: none;
}

.topbar,
.bottom-nav {
  display: none;
}

.topbar__actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}
.topbar__icon {
  display: grid;
  place-items: center;
  width: 36px;
  height: 36px;
  border-radius: var(--radius-pill);
  color: var(--color-text-muted);
}
.topbar__icon:hover {
  color: var(--color-text);
  background: var(--color-surface-2);
}

/* ---------- Mobile ---------- */
@media (max-width: 899px) {
  .sidebar {
    display: none;
  }
  .content {
    margin-left: 0;
    min-height: auto;
    padding-bottom: calc(var(--bottom-nav-height) + env(safe-area-inset-bottom));
  }
  .topbar {
    position: sticky;
    top: 0;
    z-index: var(--z-nav);
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-3) var(--space-4);
    padding-top: calc(var(--space-3) + env(safe-area-inset-top));
    background: rgb(7 13 24 / 0.85);
    backdrop-filter: blur(10px);
    border-bottom: 1px solid var(--color-hairline);
  }
  .bottom-nav {
    position: fixed;
    inset: auto 0 0 0;
    z-index: var(--z-nav);
    display: grid;
    grid-template-columns: repeat(5, 1fr);
    height: calc(var(--bottom-nav-height) + env(safe-area-inset-bottom));
    padding-bottom: env(safe-area-inset-bottom);
    background: rgb(7 13 24 / 0.92);
    backdrop-filter: blur(12px);
    border-top: 1px solid var(--color-hairline);
  }
  .bottom-nav__link {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 2px;
    font-size: 0.6875rem;
    font-weight: 500;
    color: var(--color-text-faint);
    text-decoration: none;
  }
  .bottom-nav__link.is-active {
    color: var(--color-brass-strong);
  }
}
</style>
