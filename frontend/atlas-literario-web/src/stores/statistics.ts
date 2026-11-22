import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api'
import { toApiError, type ApiError } from '@/api/errors'
import type { DashboardDto, PassportsDto } from '@/types/api'

/** Esqueleto (Etapa 2). Dashboard e Passaportes serão desenvolvidos na Etapa 5. */
export const useStatisticsStore = defineStore('statistics', () => {
  const dashboard = ref<DashboardDto | null>(null)
  const passports = ref<PassportsDto | null>(null)
  const loading = ref(false)
  const error = ref<ApiError | null>(null)

  async function run(task: () => Promise<void>) {
    loading.value = true
    error.value = null
    try {
      await task()
    } catch (e) {
      error.value = toApiError(e)
    } finally {
      loading.value = false
    }
  }

  const fetchDashboard = () =>
    run(async () => {
      dashboard.value = await api.statistics.dashboard()
    })

  const fetchPassports = () =>
    run(async () => {
      passports.value = await api.statistics.passports()
    })

  /** Mudanças na biblioteca tornam os números obsoletos (recarregados na próxima visita). */
  function invalidate() {
    dashboard.value = null
    passports.value = null
  }

  return { dashboard, passports, loading, error, fetchDashboard, fetchPassports, invalidate }
})
