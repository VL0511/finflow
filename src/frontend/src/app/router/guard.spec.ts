import { describe, expect, it } from 'vitest'
import type { RouteLocationNormalized, RouteMeta } from 'vue-router'
import { can } from '@/features/auth/permissions'
import { navigationFor } from '@/features/layout/config/navigation'
import { resolveAccess, safeRedirect, type SessionSnapshot } from './guard'

function route(name: string, meta: RouteMeta = {}, fullPath = `/${name}`): RouteLocationNormalized {
  return { name, meta, fullPath } as unknown as RouteLocationNormalized
}

const anonymous: SessionSnapshot = { isAuthenticated: false, role: null, mustChangePassword: false }
const as = (role: string, mustChangePassword = false): SessionSnapshot => ({ isAuthenticated: true, role, mustChangePassword })

describe('resolveAccess (controle visual de rotas)', () => {
  it('sem sessão: vai para o login guardando o destino', () => {
    expect(resolveAccess(route('accounts', {}, '/contas?page=2'), anonymous)).toEqual({ name: 'login', query: { redirect: '/contas?page=2' } })
  })

  it('login é público; quem já entrou volta ao início', () => {
    expect(resolveAccess(route('login', { public: true }), anonymous)).toBe(true)
    expect(resolveAccess(route('login', { public: true }), as('Customer'))).toEqual({ name: 'dashboard' })
  })

  it('troca de senha pendente bloqueia todas as outras telas', () => {
    expect(resolveAccess(route('dashboard'), as('Administrator', true))).toEqual({ name: 'change-password' })
    expect(resolveAccess(route('change-password'), as('Administrator', true))).toBe(true)
  })

  it('administração só para Administrator', () => {
    const admin = route('admin-users', { capability: 'administration' })
    expect(resolveAccess(admin, as('Administrator'))).toBe(true)
    expect(resolveAccess(admin, as('Treasurer'))).toEqual({ name: 'dashboard' })
    expect(resolveAccess(admin, as('Customer'))).toEqual({ name: 'dashboard' })
  })

  it('abrir conta: Administrator e Customer, não Treasurer', () => {
    const open = route('account-create', { capability: 'openAccount' })
    expect(resolveAccess(open, as('Customer'))).toBe(true)
    expect(resolveAccess(open, as('Treasurer'))).toEqual({ name: 'dashboard' })
  })

  it('perfil desconhecido não ganha capacidades', () => {
    expect(resolveAccess(route('transaction-create', { capability: 'banking' }), as('SuperAdmin'))).toEqual({ name: 'dashboard' })
  })
})

describe('safeRedirect', () => {
  it('aceita só caminhos internos (sem open redirect)', () => {
    expect(safeRedirect('/contas')).toBe('/contas')
    expect(safeRedirect('https://evil.example')).toBe('/')
    expect(safeRedirect('//evil.example')).toBe('/')
    expect(safeRedirect(undefined)).toBe('/')
  })
})

describe('menu por perfil', () => {
  const labels = (role: string) => navigationFor((c) => can(role, c)).flatMap((s) => s.items.map((i) => i.label))

  it('Administrator vê a área administrativa', () => {
    expect(labels('Administrator')).toEqual(expect.arrayContaining(['Usuários', 'Moedas', 'Auditoria', 'Câmbio']))
  })

  it('Treasurer e Customer não veem a área administrativa', () => {
    for (const role of ['Treasurer', 'Customer']) {
      expect(labels(role)).not.toContain('Usuários')
      expect(labels(role)).not.toContain('Auditoria')
      expect(labels(role)).toEqual(expect.arrayContaining(['Dashboard', 'Transações', 'Contas', 'Câmbio']))
    }
  })
})
