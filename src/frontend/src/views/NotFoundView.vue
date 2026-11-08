<script setup lang="ts">
import { ArrowLeft, Compass } from '@lucide/vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
</script>

<template>
  <BaseCard>
    <EmptyState
      :icon="Compass"
      title="Página não encontrada"
      description="O endereço acessado não existe ou ainda não está disponível nesta versão."
    >
      <BaseButton :icon="ArrowLeft" to="/">Voltar ao dashboard</BaseButton>
    </EmptyState>
  </BaseCard>
</template>
