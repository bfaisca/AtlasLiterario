import type { AxiosInstance } from 'axios'
import type { DashboardDto, PassportsDto } from '@/types/api'

export interface StatisticsApi {
  dashboard(): Promise<DashboardDto>
  passports(): Promise<PassportsDto>
}

export function createHttpStatisticsApi(http: AxiosInstance): StatisticsApi {
  return {
    async dashboard() {
      return (await http.get<DashboardDto>('/statistics/dashboard')).data
    },
    async passports() {
      return (await http.get<PassportsDto>('/statistics/passports')).data
    },
  }
}
