import { createRouter, createWebHistory } from 'vue-router'
import type { Capability } from '@/features/auth/permissions'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import AppLayout from '@/layouts/AppLayout.vue'
import { resolveAccess } from './guard'

declare module 'vue-router' {
  interface RouteMeta {
    title?: string
    section?: string
    /** Item do menu a destacar em sub-rotas (ex.: detalhe de conta → "Contas"). */
    navRoute?: string
    /** Rota sem autenticação (login). */
    public?: boolean
    /** Capacidade exigida (espelho das policies da API; controle apenas visual). */
    capability?: Capability
  }
}

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('@/features/auth/views/LoginView.vue'),
      meta: { title: 'Entrar', public: true },
    },
    {
      path: '/alterar-senha',
      name: 'change-password',
      component: () => import('@/features/auth/views/ChangePasswordView.vue'),
      meta: { title: 'Alterar senha' },
    },
    {
      path: '/',
      component: AppLayout,
      children: [
        {
          path: '',
          name: 'dashboard',
          component: () => import('@/features/dashboard/views/DashboardView.vue'),
          meta: { title: 'Dashboard', section: 'Geral' },
        },
        {
          path: 'transacoes',
          name: 'transactions',
          component: () => import('@/features/transactions/views/TransactionsView.vue'),
          meta: { title: 'Transações', section: 'Operações' },
        },
        {
          path: 'transacoes/nova',
          name: 'transaction-create',
          component: () => import('@/features/transactions/views/CreateTransferView.vue'),
          meta: { title: 'Nova transferência', section: 'Transações', navRoute: 'transactions', capability: 'banking' },
        },
        {
          path: 'transacoes/:id',
          name: 'transaction-details',
          component: () => import('@/features/transactions/views/TransactionDetailView.vue'),
          props: true,
          meta: { title: 'Detalhes da transação', section: 'Transações', navRoute: 'transactions' },
        },
        {
          path: 'contas',
          name: 'accounts',
          component: () => import('@/features/accounts/views/AccountsView.vue'),
          meta: { title: 'Contas', section: 'Operações' },
        },
        {
          path: 'contas/nova',
          name: 'account-create',
          component: () => import('@/features/accounts/views/CreateAccountView.vue'),
          meta: { title: 'Nova conta', section: 'Contas', navRoute: 'accounts', capability: 'openAccount' },
        },
        {
          path: 'contas/:id',
          name: 'account-details',
          component: () => import('@/features/accounts/views/AccountDetailView.vue'),
          props: true,
          meta: { title: 'Detalhes da conta', section: 'Contas', navRoute: 'accounts' },
        },
        {
          path: 'cambio',
          name: 'exchange-rates',
          component: () => import('@/features/currencies/views/ExchangeRatesView.vue'),
          meta: { title: 'Câmbio', section: 'Operações' },
        },
        {
          path: 'admin/usuarios',
          name: 'admin-users',
          component: () => import('@/features/admin/views/UsersView.vue'),
          meta: { title: 'Usuários', section: 'Administração', capability: 'administration' },
        },
        {
          path: 'admin/moedas',
          name: 'admin-currencies',
          component: () => import('@/features/currencies/views/CurrenciesView.vue'),
          meta: { title: 'Moedas', section: 'Administração', capability: 'administration' },
        },
        {
          path: 'admin/auditoria',
          name: 'admin-audit',
          component: () => import('@/features/admin/views/AuditView.vue'),
          meta: { title: 'Auditoria', section: 'Administração', capability: 'administration' },
        },
        {
          path: ':pathMatch(.*)*',
          name: 'not-found',
          component: () => import('@/views/NotFoundView.vue'),
          meta: { title: 'Página não encontrada' },
        },
      ],
    },
  ],
  scrollBehavior: () => ({ top: 0 }),
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.loadSession()
  return resolveAccess(to, {
    isAuthenticated: auth.isAuthenticated,
    role: auth.role,
    mustChangePassword: auth.mustChangePassword,
  })
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · FinFlow` : 'FinFlow'
})
