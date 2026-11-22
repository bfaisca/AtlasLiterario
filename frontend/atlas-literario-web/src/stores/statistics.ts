import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api'
import { toApiError, type ApiError } from '@/api/errors'
import type { DashboardDto, PassportsDto } from '@/types/api'

/**
 * Estatísticas do usuário (api-contracts §8): Dashboard e Passaportes, cada um com seu próprio
 * estado de carregamento/erro (um erro no painel não aparece nos Passaportes e vice-versa).
 * Respostas fora de ordem e de uma sessão anterior (`reset`) são descartadas.
 */
export const useStatisticsStore = defineStore('statistics', () => {
  const dashboard = ref<DashboardDto | null>(null)
  const passports = ref<PassportsDto | null>(null)
  const dashboardLoading = ref(false)
  const dashboardError = ref<ApiError | null>(null)
  const passportsLoading = ref(false)
  const passportsError = ref<ApiError | null>(null)
  /** Incrementado por `reset()`: respostas de uma geração anterior (outro usuário) são ignoradas. */
  let generation = 0
  let dashboardSeq = 0
  let passportsSeq = 0

  async function fetchDashboard() {
    const gen = generation
    const seq = ++dashboardSeq
    const isCurrent = () => gen === generation && seq === dashboardSeq
    dashboardLoading.value = true
    dashboardError.value = null
    try {
      const result = await api.statistics.dashboard()
      if (isCurrent()) dashboard.value = result
    } catch (e) {
      if (isCurrent()) dashboardError.value = toApiError(e)
    } finally {
      if (isCurrent()) dashboardLoading.value = false
    }
  }

  async function fetchPassports() {
    const gen = generation
    const seq = ++passportsSeq
    const isCurrent = () => gen === generation && seq === passportsSeq
    passportsLoading.value = true
    passportsError.value = null
    try {
      const result = await api.statistics.passports()
      if (isCurrent()) passports.value = result
    } catch (e) {
      if (isCurrent()) passportsError.value = toApiError(e)
    } finally {
      if (isCurrent()) passportsLoading.value = false
    }
  }

  /** Mudanças na biblioteca tornam os números obsoletos (recarregados na próxima visita). */
  function invalidate() {
    dashboard.value = null
    passports.value = null
  }

  /** Troca/encerramento de sessão: zera tudo e descarta respostas em voo do usuário anterior. */
  function reset() {
    generation++
    dashboard.value = null
    passports.value = null
    dashboardLoading.value = false
    dashboardError.value = null
    passportsLoading.value = false
    passportsError.value = null
  }

  return {
    dashboard,
    passports,
    dashboardLoading,
    dashboardError,
    passportsLoading,
    passportsError,
    fetchDashboard,
    fetchPassports,
    invalidate,
    reset,
  }
})
