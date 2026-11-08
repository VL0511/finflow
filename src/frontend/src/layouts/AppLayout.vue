<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { watch } from 'vue'
import { useRoute } from 'vue-router'
import AppHeader from '@/features/layout/components/AppHeader.vue'
import AppSidebar from '@/features/layout/components/AppSidebar.vue'
import { useLayoutStore } from '@/features/layout/stores/useLayoutStore'
import { useTransactionsStore } from '@/features/transactions/stores/useTransactionsStore'

const layout = useLayoutStore()
const { isMobileNavOpen, isSidebarCollapsed } = storeToRefs(layout)
const route = useRoute()

// O contador de transações em andamento do menu vem do resumo global (GET /transactions/summary),
// em qualquer página. Deduplicado: se a view também pedir o resumo, sai uma única requisição.
void useTransactionsStore().ensureSummary()

// Fecha o drawer ao navegar.
watch(() => route.fullPath, layout.closeMobileNav)
</script>

<template>
  <div class="min-h-dvh">
    <a
      href="#conteudo"
      class="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-lg focus:bg-surface focus:px-3 focus:py-2 focus:shadow-pop"
    >
      Pular para o conteúdo
    </a>

    <!-- Sidebar fixa (desktop) -->
    <div class="fixed inset-y-0 left-0 z-40 hidden transition-[width] duration-200 lg:block">
      <AppSidebar :collapsed="isSidebarCollapsed" @toggle="layout.toggleSidebar" />
    </div>

    <!-- Drawer (mobile / tablet) -->
    <Transition
      enter-active-class="transition-opacity duration-200"
      leave-active-class="transition-opacity duration-150"
      enter-from-class="opacity-0"
      leave-to-class="opacity-0"
    >
      <div
        v-if="isMobileNavOpen"
        class="fixed inset-0 z-40 bg-ink/50 backdrop-blur-[2px] lg:hidden"
        aria-hidden="true"
        @click="layout.closeMobileNav"
      />
    </Transition>
    <Transition
      enter-active-class="transition-transform duration-200 ease-out"
      leave-active-class="transition-transform duration-150 ease-in"
      enter-from-class="-translate-x-full"
      leave-to-class="-translate-x-full"
    >
      <div
        v-if="isMobileNavOpen"
        class="fixed inset-y-0 left-0 z-50 shadow-pop lg:hidden"
        role="dialog"
        aria-modal="true"
        aria-label="Menu de navegação"
        @keydown.esc="layout.closeMobileNav"
      >
        <AppSidebar mobile @close="layout.closeMobileNav" />
      </div>
    </Transition>

    <!-- Conteúdo -->
    <div
      class="flex min-h-dvh flex-col transition-[padding] duration-200"
      :class="isSidebarCollapsed ? 'lg:pl-[76px]' : 'lg:pl-64'"
    >
      <AppHeader @open-menu="layout.openMobileNav" />
      <main id="conteudo" class="mx-auto w-full max-w-[1440px] flex-1 px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
        <RouterView />
      </main>
    </div>
  </div>
</template>
