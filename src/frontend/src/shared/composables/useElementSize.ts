import { onBeforeUnmount, onMounted, ref, type Ref } from 'vue'

/** Observa a largura/altura reais de um elemento (para gráficos SVG responsivos). */
export function useElementSize(target: Ref<HTMLElement | null>) {
  const width = ref(0)
  const height = ref(0)
  let observer: ResizeObserver | null = null

  onMounted(() => {
    if (!target.value) return
    observer = new ResizeObserver(([entry]) => {
      if (!entry) return
      width.value = entry.contentRect.width
      height.value = entry.contentRect.height
    })
    observer.observe(target.value)
  })

  onBeforeUnmount(() => observer?.disconnect())

  return { width, height }
}
