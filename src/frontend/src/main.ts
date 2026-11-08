import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import { router } from './app/router'
import { useAuthStore } from './features/auth/stores/useAuthStore'
import { onUnauthorized } from './shared/api'
import './assets/styles/main.css'

const app = createApp(App).use(createPinia())

// 401 em qualquer chamada (fora do login) = sessão expirada: volta ao login, descartando o estado.
const auth = useAuthStore()
onUnauthorized(() => auth.handleSessionExpired())

app.use(router).mount('#app')
