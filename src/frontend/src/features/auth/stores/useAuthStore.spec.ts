import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/shared/api'
import { authApi, type SessionUser } from '../api/authApi'
import { useAuthStore } from './useAuthStore'

vi.mock('../api/authApi', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn(), changePassword: vi.fn() },
}))

const api = vi.mocked(authApi)

const user = (overrides: Partial<SessionUser> = {}): SessionUser => ({
  id: 'u1',
  email: 'tesouraria@finflow.com',
  fullName: 'Maria Tesoureira',
  role: 'Treasurer',
  mustChangePassword: false,
  ...overrides,
})

describe('useAuthStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('carrega a sessão do servidor uma única vez (deduplicado)', async () => {
    api.me.mockResolvedValue(user())
    const auth = useAuthStore()

    await Promise.all([auth.loadSession(), auth.loadSession()])
    await auth.loadSession()

    expect(api.me).toHaveBeenCalledTimes(1)
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.roleLabel).toBe('Tesouraria')
  })

  it('401 em /me = sem sessão (anônimo)', async () => {
    api.me.mockRejectedValue(new ApiError({ kind: 'http', status: 401, title: 'Sessão ausente.' }))
    const auth = useAuthStore()

    await auth.loadSession()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.status).toBe('anonymous')
  })

  it('login guarda o usuário devolvido pelo servidor (perfil vem da API, não do cliente)', async () => {
    api.login.mockResolvedValue(user({ role: 'Customer' }))
    const auth = useAuthStore()

    await auth.login({ email: 'cliente@finflow.com', password: 'x', rememberMe: false })

    expect(api.login).toHaveBeenCalledWith({ email: 'cliente@finflow.com', password: 'x', rememberMe: false })
    expect(auth.role).toBe('Customer')
    expect(auth.hasCapability('administration')).toBe(false)
    expect(auth.hasCapability('openAccount')).toBe(true)
  })

  it('credenciais inválidas: propaga o erro e continua anônimo', async () => {
    const invalid = new ApiError({ kind: 'http', status: 401, title: 'E-mail ou senha inválidos.' })
    api.login.mockRejectedValue(invalid)
    const auth = useAuthStore()

    await expect(auth.login({ email: 'a@b.c', password: 'errada', rememberMe: false })).rejects.toBe(invalid)
    expect(auth.isAuthenticated).toBe(false)
  })

  it('primeiro login: troca de senha pendente até a API confirmar a troca', async () => {
    api.login.mockResolvedValue(user({ role: 'Administrator', mustChangePassword: true }))
    api.changePassword.mockResolvedValue(user({ role: 'Administrator', mustChangePassword: false }))
    const auth = useAuthStore()

    await auth.login({ email: 'admin@finflow.com', password: 'admin', rememberMe: false })
    expect(auth.mustChangePassword).toBe(true)

    await auth.changePassword({ currentPassword: 'admin', newPassword: 'Nova#Senha2026' })
    expect(auth.mustChangePassword).toBe(false)
  })
})
