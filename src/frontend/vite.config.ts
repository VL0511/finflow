/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    // Testes de lógica pura (stores, composables, utils): sem DOM, sem rede — a API é mockada.
    environment: 'node',
    include: ['src/**/*.spec.ts'],
  },
  server: {
    proxy: {
      // FinFlow.API (perfil "http" do launchSettings). O prefixo é mantido: /api/accounts → :5017/api/accounts.
      '/api': {
        target: 'http://localhost:5017',
        changeOrigin: true,
      },
    },
  },
})
