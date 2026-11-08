import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'
import { isApiError } from '@/shared/api'
import { authApi, type ChangePasswordRequest, type LoginRequest, type SessionUser } from '../api/authApi'
import { can, ROLE_LABELS, isRole, type Capability } from '../permissions'

export type SessionStatus = 'unknown' | 'authenticated' | 'anonymous'

/**
 * Sessão do usuário. A fonte da verdade é o cookie no servidor: o store só guarda o que
 * GET /api/auth/me devolveu, para a UI decidir o que exibir.
 */
export const useAuthStore = defineStore('auth', () => {
  const user = shallowRef<SessionUser | null>(null)
  const status = ref<SessionStatus>('unknown')
  /** Motivo de ter caído no login (ex.: sessão expirada), exibido na tela de login. */
  const notice = ref<string | null>(null)

  let pendingSession: Promise<void> | null = null

  /** Verifica a sessão uma vez (deduplicado); chamadas seguintes reaproveitam o resultado. */
  function loadSession(): Promise<void> {
    if (status.value !== 'unknown') return Promise.resolve()
    pendingSession ??= authApi
      .me()
      .then((me) => {
        user.value = me
        status.value = 'authenticated'
      })
      .catch((error: unknown) => {
        if (isApiError(error) && error.kind === 'canceled') return
        // 401 = sem sessão. Outros erros (API fora do ar): trata como anônimo; o login mostrará o erro.
        user.value = null
        status.value = 'anonymous'
      })
      .finally(() => (pendingSession = null))
    return pendingSession
  }

  async function login(request: LoginRequest): Promise<SessionUser> {
    const me = await authApi.login(request)
    user.value = me
    status.value = 'authenticated'
    notice.value = null
    return me
  }

  async function changePassword(request: ChangePasswordRequest): Promise<SessionUser> {
    const me = await authApi.changePassword(request)
    user.value = me
    return me
  }

  /**
   * Encerra a sessão e recarrega a aplicação no login: descarta todo o estado em memória
   * (contas, transações...) do usuário anterior — nada vaza para o próximo login na mesma aba.
   */
  async function logout(): Promise<void> {
    try {
      await authApi.logout()
    } finally {
      leave('/login')
    }
  }

  /** Chamado pela camada HTTP em 401 fora do login: a sessão expirou. */
  function handleSessionExpired() {
    if (status.value !== 'authenticated') return
    status.value = 'anonymous'
    const redirect = encodeURIComponent(window.location.pathname + window.location.search)
    leave(`/login?expirada=1&redirect=${redirect}`)
  }

  function leave(url: string) {
    user.value = null
    status.value = 'anonymous'
    window.location.assign(url)
  }

  const isAuthenticated = computed(() => status.value === 'authenticated' && user.value !== null)
  const role = computed(() => user.value?.role ?? null)
  const roleLabel = computed(() => (isRole(role.value) ? ROLE_LABELS[role.value] : ''))
  const mustChangePassword = computed(() => user.value?.mustChangePassword ?? false)

  function hasCapability(capability: Capability): boolean {
    return can(role.value, capability)
  }

  return {
    user,
    status,
    notice,
    isAuthenticated,
    role,
    roleLabel,
    mustChangePassword,
    hasCapability,
    loadSession,
    login,
    changePassword,
    logout,
    handleSessionExpired,
  }
})
