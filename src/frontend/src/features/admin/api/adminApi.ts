import { api } from '@/shared/api'
import type { PagedResult } from '@/shared/api/pagination'

/** Espelha UserResponse. */
export interface AdminUser {
  id: string
  email: string
  fullName: string
  role: string
  isActive: boolean
  mustChangePassword: boolean
  isLockedOut: boolean
  createdAt: string
}

export interface CreateUserRequest {
  email: string
  fullName: string
  password: string
  role: string
  requirePasswordChange: boolean
}

/** Espelha AuditLogResponse. `details` é JSON. */
export interface AuditLog {
  id: string
  occurredAt: string
  userId: string | null
  userEmail: string | null
  action: string
  entityType: string
  entityId: string | null
  result: string
  details: string
}

export interface AuditQuery {
  action?: string
  entityType?: string
  entityId?: string
  userId?: string
}

/** Endpoints administrativos (somente Administrator; a API devolve 403 para os demais perfis). */
export const adminApi = {
  listUsers(params: { page: number; pageSize: number; search?: string; role?: string }, signal?: AbortSignal): Promise<PagedResult<AdminUser>> {
    return api.get<PagedResult<AdminUser>>('/admin/users', { params, signal })
  },
  createUser(request: CreateUserRequest): Promise<AdminUser> {
    return api.post<AdminUser>('/admin/users', request)
  },
  updateUser(id: string, request: { fullName?: string; role?: string; isActive?: boolean }): Promise<AdminUser> {
    return api.patch<AdminUser>(`/admin/users/${encodeURIComponent(id)}`, request)
  },
  resetPassword(id: string, newPassword: string): Promise<void> {
    return api.post<void>(`/admin/users/${encodeURIComponent(id)}/reset-password`, { newPassword })
  },
  auditLogs(params: AuditQuery & { page: number; pageSize: number }, signal?: AbortSignal): Promise<PagedResult<AuditLog>> {
    return api.get<PagedResult<AuditLog>>('/admin/audit-logs', { params, signal })
  },
}
