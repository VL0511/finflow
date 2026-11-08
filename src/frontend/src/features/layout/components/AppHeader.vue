<script setup lang="ts">
import { ChevronDown, KeyRound, LogOut, Menu } from '@lucide/vue'
import { computed, onBeforeUnmount, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import Avatar from '@/shared/components/ui/Avatar.vue'
import BaseBadge from '@/shared/components/ui/BaseBadge.vue'

const emit = defineEmits<{ openMenu: [] }>()

const route = useRoute()
const auth = useAuthStore()
const section = computed(() => route.meta.section ?? 'FinFlow')
const pageTitle = computed(() => route.meta.title ?? '')

const menuOpen = ref(false)
const menuRoot = ref<HTMLElement | null>(null)

function onDocumentClick(event: MouseEvent) {
  if (menuRoot.value && !menuRoot.value.contains(event.target as Node)) menuOpen.value = false
}
document.addEventListener('click', onDocumentClick)
onBeforeUnmount(() => document.removeEventListener('click', onDocumentClick))
</script>

<template>
  <header
    class="sticky top-0 z-30 flex h-16 shrink-0 items-center gap-3 border-b border-line bg-surface/85 px-4 backdrop-blur-md sm:px-6 lg:px-8"
  >
    <button
      type="button"
      class="-ml-1 rounded-lg p-2 text-ink-secondary hover:bg-neutral-soft hover:text-ink lg:hidden"
      aria-label="Abrir menu"
      @click="emit('openMenu')"
    >
      <Menu class="size-5" aria-hidden="true" />
    </button>

    <!-- Breadcrumb -->
    <nav aria-label="Breadcrumb" class="min-w-0 flex-1">
      <ol class="flex items-center gap-2 text-sm">
        <li class="hidden text-ink-muted sm:block">{{ section }}</li>
        <li class="hidden text-line-strong sm:block" aria-hidden="true">/</li>
        <li class="truncate font-medium text-ink" aria-current="page">{{ pageTitle }}</li>
      </ol>
    </nav>

    <div class="flex items-center gap-1 sm:gap-2">
      <BaseBadge tone="brand" dot class="hidden md:inline-flex">Sandbox</BaseBadge>

      <div class="mx-1 hidden h-6 w-px bg-line sm:block" aria-hidden="true" />

      <div v-if="auth.user" ref="menuRoot" class="relative">
        <button
          type="button"
          class="flex items-center gap-2.5 rounded-lg py-1 pr-1.5 pl-1 transition-colors hover:bg-neutral-soft"
          aria-label="Menu do usuário"
          aria-haspopup="menu"
          :aria-expanded="menuOpen"
          @click="menuOpen = !menuOpen"
        >
          <Avatar :name="auth.user.fullName || auth.user.email" size="sm" />
          <span class="hidden text-left leading-tight sm:block">
            <span class="block text-[13px] font-medium text-ink">{{ auth.user.fullName || auth.user.email }}</span>
            <span class="block text-[11px] text-ink-muted">{{ auth.roleLabel }}</span>
          </span>
          <ChevronDown class="hidden size-4 text-ink-muted sm:block" aria-hidden="true" />
        </button>

        <div
          v-if="menuOpen"
          role="menu"
          class="absolute right-0 mt-2 w-60 rounded-xl border border-line bg-surface p-1.5 shadow-pop"
          @keydown.esc="menuOpen = false"
        >
          <p class="truncate px-2.5 pt-1.5 pb-2 text-xs text-ink-muted">{{ auth.user.email }}</p>
          <RouterLink
            role="menuitem"
            :to="{ name: 'change-password' }"
            class="flex items-center gap-2 rounded-lg px-2.5 py-2 text-[13px] text-ink hover:bg-neutral-soft"
            @click="menuOpen = false"
          >
            <KeyRound class="size-4 text-ink-muted" aria-hidden="true" /> Alterar senha
          </RouterLink>
          <button
            type="button"
            role="menuitem"
            class="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-[13px] text-critical hover:bg-critical-soft"
            @click="auth.logout()"
          >
            <LogOut class="size-4" aria-hidden="true" /> Sair
          </button>
        </div>
      </div>
    </div>
  </header>
</template>
