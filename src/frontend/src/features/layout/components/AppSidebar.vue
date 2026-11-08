<script setup lang="ts">
import { FlaskConical, PanelLeftClose, PanelLeftOpen, X } from '@lucide/vue'
import { computed } from 'vue'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import { useTransactionsStore } from '@/features/transactions/stores/useTransactionsStore'
import { navigationFor, type NavItem } from '../config/navigation'
import BrandLogo from './BrandLogo.vue'
import SidebarNavItem from './SidebarNavItem.vue'

withDefaults(
  defineProps<{
    collapsed?: boolean
    /** Variante drawer (mobile): mostra botão de fechar em vez de recolher. */
    mobile?: boolean
  }>(),
  { collapsed: false, mobile: false },
)

const emit = defineEmits<{ toggle: []; close: [] }>()

const transactions = useTransactionsStore()
const auth = useAuthStore()
// Menu conforme o perfil da sessão (só visual: a API recusa o que o perfil não pode).
const sections = computed(() => navigationFor(auth.hasCapability))

function badgeFor(item: NavItem): number {
  return item.badgeKey === 'inFlight' ? transactions.inFlightCount : 0
}
</script>

<template>
  <aside class="flex h-full flex-col bg-nav text-nav-ink" :class="collapsed ? 'w-[76px]' : 'w-64'">
    <!-- Marca -->
    <div class="flex h-16 shrink-0 items-center px-5" :class="collapsed ? 'justify-center px-0' : 'justify-between'">
      <RouterLink to="/" aria-label="FinFlow — início">
        <BrandLogo :compact="collapsed" />
      </RouterLink>
      <button
        v-if="mobile"
        type="button"
        class="rounded-lg p-1.5 text-nav-ink-muted hover:bg-nav-raised hover:text-white"
        aria-label="Fechar menu"
        @click="emit('close')"
      >
        <X class="size-5" aria-hidden="true" />
      </button>
    </div>

    <!-- Navegação principal -->
    <nav class="flex-1 overflow-y-auto px-3 pt-2 pb-4" aria-label="Principal">
      <div v-for="section in sections" :key="section.title" class="mb-5 last:mb-0">
        <p
          v-if="!collapsed"
          class="mb-1.5 px-2.5 text-[11px] font-semibold tracking-wider text-nav-ink-muted/80 uppercase"
        >
          {{ section.title }}
        </p>
        <div v-else class="mx-auto mb-2 h-px w-6 bg-nav-line" aria-hidden="true" />
        <ul class="space-y-0.5">
          <li v-for="item in section.items" :key="item.label">
            <SidebarNavItem :item="item" :collapsed="collapsed" :badge="badgeFor(item)" />
          </li>
        </ul>
      </div>
    </nav>

    <!-- Rodapé -->
    <div class="shrink-0 space-y-3 border-t border-nav-line px-3 py-4">
      <!-- Aviso honesto: os dados vêm da API de desenvolvimento, com contas fictícias -->
      <div v-if="!collapsed" class="rounded-xl border border-nav-line bg-nav-raised p-3">
        <p class="flex items-center gap-2 text-xs font-semibold text-white">
          <FlaskConical class="size-3.5 text-brand-400" aria-hidden="true" />
          Ambiente de desenvolvimento
        </p>
        <p class="mt-1 text-[11.5px] leading-relaxed text-nav-ink-muted">
          Dados reais da API local, com contas fictícias.
        </p>
      </div>

      <button
        v-if="!mobile"
        type="button"
        class="flex h-9 w-full items-center gap-3 rounded-lg px-2.5 text-[13px] font-medium text-nav-ink-muted transition-colors hover:bg-nav-raised hover:text-white"
        :class="collapsed && 'justify-center px-0'"
        :aria-label="collapsed ? 'Expandir menu' : 'Recolher menu'"
        :title="collapsed ? 'Expandir menu' : undefined"
        @click="emit('toggle')"
      >
        <PanelLeftOpen v-if="collapsed" class="size-[18px]" aria-hidden="true" />
        <template v-else>
          <PanelLeftClose class="size-[18px]" aria-hidden="true" />
          Recolher
        </template>
      </button>
    </div>
  </aside>
</template>
