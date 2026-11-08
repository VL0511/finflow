import { ArrowLeftRight, Coins, Landmark, LayoutDashboard, ScrollText, Users, WalletCards } from '@lucide/vue'
import type { Component } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import type { Capability } from '@/features/auth/permissions'

export interface NavItem {
  label: string
  icon: Component
  /** Ausente = item ainda não disponível (exibido como "Em breve"). */
  to?: RouteLocationRaw
  /** Nome da rota para destacar o item ativo. */
  routeName?: string
  /** Contador opcional ao lado do item (ex.: transações na fila). */
  badgeKey?: 'inFlight'
  /** Só aparece para quem tem a capacidade (controle visual; a API reaplica as permissões). */
  capability?: Capability
}

export interface NavSection {
  title: string
  items: NavItem[]
}

/** Menu declarativo: adicionar uma página é adicionar uma linha aqui + a rota. */
export const navigation: NavSection[] = [
  {
    title: 'Geral',
    items: [{ label: 'Dashboard', icon: LayoutDashboard, to: { name: 'dashboard' }, routeName: 'dashboard' }],
  },
  {
    title: 'Operações',
    items: [
      { label: 'Transações', icon: ArrowLeftRight, to: { name: 'transactions' }, routeName: 'transactions', badgeKey: 'inFlight' },
      { label: 'Contas', icon: Landmark, to: { name: 'accounts' }, routeName: 'accounts' },
      { label: 'Câmbio', icon: WalletCards, to: { name: 'exchange-rates' }, routeName: 'exchange-rates' },
    ],
  },
  {
    title: 'Administração',
    items: [
      { label: 'Usuários', icon: Users, to: { name: 'admin-users' }, routeName: 'admin-users', capability: 'administration' },
      { label: 'Moedas', icon: Coins, to: { name: 'admin-currencies' }, routeName: 'admin-currencies', capability: 'administration' },
      { label: 'Auditoria', icon: ScrollText, to: { name: 'admin-audit' }, routeName: 'admin-audit', capability: 'administration' },
    ],
  },
]

/** Seções com os itens permitidos ao perfil; seções vazias somem. */
export function navigationFor(allowed: (capability: Capability) => boolean): NavSection[] {
  return navigation
    .map((section) => ({ ...section, items: section.items.filter((item) => !item.capability || allowed(item.capability)) }))
    .filter((section) => section.items.length > 0)
}
