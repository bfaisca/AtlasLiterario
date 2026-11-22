import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import type { UserRole } from '@/types/api'
import ProfilePage from '../ProfilePage.vue'

function mountAs(role: UserRole) {
  setActivePinia(createPinia())
  useAuthStore().user = {
    id: 'u',
    name: 'Ana',
    email: 'ana@x.com',
    mapVisibility: 'Private',
    role,
    createdAt: '2025-01-01T00:00:00Z',
  }
  const router = createRouter({ history: createMemoryHistory(), routes: [] })
  return mount(ProfilePage, { global: { plugins: [router] } })
}

describe('ProfilePage: papel', () => {
  it('moderador vê o selo "Moderador"', () => {
    expect(mountAs('Moderator').find('h1').text()).toContain('Moderador')
  })
  it('usuário comum não vê selo de papel', () => {
    const wrapper = mountAs('User')
    expect(wrapper.find('h1').text()).toBe('Ana')
    expect(wrapper.text()).not.toContain('Moderador')
  })
})
