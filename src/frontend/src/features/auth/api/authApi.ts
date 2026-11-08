import { api } from '@/shared/api'

/** Espelha MeResponse (GET /api/auth/me). */
export interface SessionUser {
  id: string
  email: string
  fullName: string
  /** Informativo: o backend sempre reaplica as permissões. */
  role: string
  mustChangePassword: boolean
}

export interface LoginRequest {
  email: string
  password: string
  rememberMe: boolean
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

/** Endpoints de AuthController. A sessão é um cookie HttpOnly gerenciado pelo navegador. */
export const authApi = {
  /** 200 com o usuário; 401 credenciais inválidas; 423 bloqueado; 429 muitas tentativas. */
  login(request: LoginRequest): Promise<SessionUser> {
    return api.post<SessionUser>('/auth/login', request)
  },
  logout(): Promise<void> {
    return api.post<void>('/auth/logout')
  },
  /** 401 quando não há sessão (ou ela expirou). */
  me(): Promise<SessionUser> {
    return api.get<SessionUser>('/auth/me')
  },
  changePassword(request: ChangePasswordRequest): Promise<SessionUser> {
    return api.post<SessionUser>('/auth/change-password', request)
  },
}
