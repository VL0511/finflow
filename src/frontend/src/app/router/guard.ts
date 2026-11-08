import type { RouteLocationNormalized, RouteLocationRaw } from 'vue-router'
import { can, type Capability } from '@/features/auth/permissions'

export interface SessionSnapshot {
  isAuthenticated: boolean
  role: string | null
  mustChangePassword: boolean
}

/**
 * Decide a navegação (função pura, testável). Controle VISUAL apenas: a API recusa (401/403)
 * qualquer chamada indevida, independentemente do que o roteador deixar abrir.
 *
 * - rota pública (login): quem já está logado vai para o início;
 * - sem sessão: login, guardando o destino;
 * - troca de senha pendente: só a tela de troca;
 * - sem a capacidade exigida pela rota: início.
 */
export function resolveAccess(to: RouteLocationNormalized, session: SessionSnapshot): true | RouteLocationRaw {
  if (to.meta.public) {
    return session.isAuthenticated && !session.mustChangePassword ? { name: 'dashboard' } : true
  }

  if (!session.isAuthenticated) {
    return to.fullPath === '/' ? { name: 'login' } : { name: 'login', query: { redirect: to.fullPath } }
  }

  if (session.mustChangePassword && to.name !== 'change-password') return { name: 'change-password' }

  const capability = to.meta.capability as Capability | undefined
  if (capability && !can(session.role, capability)) return { name: 'dashboard' }

  return true
}

/** Só aceita redirecionar para caminhos internos (evita open redirect via ?redirect=). */
export function safeRedirect(value: unknown): string {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') ? value : '/'
}
