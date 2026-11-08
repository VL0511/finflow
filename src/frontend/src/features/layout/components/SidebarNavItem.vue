<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import type { NavItem } from '../config/navigation'

const props = withDefaults(defineProps<{ item: NavItem; collapsed?: boolean; badge?: number }>(), {
  collapsed: false,
  badge: 0,
})

const route = useRoute()
const isActive = computed(
  () => !!props.item.routeName && (route.name === props.item.routeName || route.meta.navRoute === props.item.routeName),
)
const isAvailable = computed(() => !!props.item.to)

const baseClasses =
  'group relative flex h-9 items-center gap-3 rounded-lg px-2.5 text-[13.5px] font-medium transition-colors'
</script>

<template>
  <RouterLink
    v-if="isAvailable"
    :to="item.to!"
    :class="[
      baseClasses,
      collapsed && 'justify-center px-0',
      isActive ? 'bg-nav-raised text-white' : 'text-nav-ink hover:bg-nav-raised/60 hover:text-white',
    ]"
    :aria-current="isActive ? 'page' : undefined"
    :title="collapsed ? item.label : undefined"
  >
    <span
      v-if="isActive"
      class="absolute top-1/2 -left-3 h-5 w-[3px] -translate-y-1/2 rounded-r-full bg-brand-400"
      aria-hidden="true"
    />
    <component
      :is="item.icon"
      class="size-[18px] shrink-0"
      :class="isActive ? 'text-brand-400' : 'text-nav-ink-muted group-hover:text-nav-ink'"
      aria-hidden="true"
    />
    <span v-if="!collapsed" class="truncate">{{ item.label }}</span>
    <span
      v-if="badge > 0 && !collapsed"
      class="ml-auto rounded-full bg-brand-500/15 px-1.5 py-px text-[11px] font-semibold text-brand-400 tabular"
    >
      {{ badge }}
      <span class="sr-only">na fila</span>
    </span>
    <span
      v-else-if="badge > 0"
      class="absolute top-1.5 right-1.5 size-1.5 rounded-full bg-brand-400"
      aria-hidden="true"
    />
  </RouterLink>

  <!-- Item ainda não disponível: visível para comunicar o roadmap, mas inerte. -->
  <span
    v-else
    :class="[baseClasses, collapsed && 'justify-center px-0', 'cursor-not-allowed text-nav-ink-muted/70']"
    :title="`${item.label} — em breve`"
    aria-disabled="true"
  >
    <component :is="item.icon" class="size-[18px] shrink-0" aria-hidden="true" />
    <span v-if="!collapsed" class="truncate">{{ item.label }}</span>
  </span>
</template>
