import type { AxiosInstance } from 'axios'
import type {
  AuthResponse,
  ChangePasswordRequest,
  ForgotPasswordRequest,
  LoginRequest,
  RefreshRequest,
  RegisterRequest,
  ResetPasswordRequest,
  UpdateProfileRequest,
  UserProfileDto,
} from '@/types/api'

export interface AuthApi {
  register(request: RegisterRequest): Promise<AuthResponse>
  login(request: LoginRequest): Promise<AuthResponse>
  refresh(request: RefreshRequest): Promise<AuthResponse>
  logout(request: RefreshRequest): Promise<void>
  forgotPassword(request: ForgotPasswordRequest): Promise<void>
  resetPassword(request: ResetPasswordRequest): Promise<void>
  me(): Promise<UserProfileDto>
  updateMe(request: UpdateProfileRequest): Promise<UserProfileDto>
  changePassword(request: ChangePasswordRequest): Promise<void>
}

export function createHttpAuthApi(http: AxiosInstance): AuthApi {
  return {
    async register(request) {
      return (await http.post<AuthResponse>('/auth/register', request)).data
    },
    async login(request) {
      return (await http.post<AuthResponse>('/auth/login', request)).data
    },
    async refresh(request) {
      return (await http.post<AuthResponse>('/auth/refresh', request, { skipAuthRefresh: true }))
        .data
    },
    async logout(request) {
      await http.post('/auth/logout', request, { skipAuthRefresh: true })
    },
    async forgotPassword(request) {
      await http.post('/auth/forgot-password', request)
    },
    async resetPassword(request) {
      await http.post('/auth/reset-password', request)
    },
    async me() {
      return (await http.get<UserProfileDto>('/auth/me')).data
    },
    async updateMe(request) {
      return (await http.put<UserProfileDto>('/auth/me', request)).data
    },
    async changePassword(request) {
      await http.put('/auth/me/password', request)
    },
  }
}
