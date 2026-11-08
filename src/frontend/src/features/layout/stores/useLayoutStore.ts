import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

const COLLAPSED_KEY = 'finflow:sidebar-collapsed'

function readCollapsed(): boolean {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === '1'
  } catch {
    return false
  }
}

/** Estado de UI do shell: drawer no mobile, sidebar recolhida no desktop. */
export const useLayoutStore = defineStore('layout', () => {
  const isMobileNavOpen = ref(false)
  const isSidebarCollapsed = ref(readCollapsed())

  watch(isSidebarCollapsed, (collapsed) => {
    try {
      localStorage.setItem(COLLAPSED_KEY, collapsed ? '1' : '0')
    } catch {
      /* preferência apenas de conveniência */
    }
  })

  const openMobileNav = () => (isMobileNavOpen.value = true)
  const closeMobileNav = () => (isMobileNavOpen.value = false)
  const toggleSidebar = () => (isSidebarCollapsed.value = !isSidebarCollapsed.value)

  return { isMobileNavOpen, isSidebarCollapsed, openMobileNav, closeMobileNav, toggleSidebar }
})
