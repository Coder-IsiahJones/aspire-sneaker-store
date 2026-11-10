<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ colors: string[] }>()

const upper = computed(() => props.colors[0] ?? '#F4F4F4')
const overlay = computed(() => props.colors[1] ?? '#141414')
const sole = computed(() => props.colors[2] ?? '#F4F4F4')
</script>

<template>
  <svg class="shoe" viewBox="0 0 240 120" role="img" aria-hidden="true">
    <ellipse cx="124" cy="112" rx="104" ry="5" fill="rgba(20, 20, 20, 0.16)" />

    <g stroke="currentColor" stroke-width="1.6" stroke-linejoin="round">
      <!-- midsole and outsole -->
      <path :fill="sole" d="M16 92 Q16 88 20 88 L224 88 Q232 88 232 95 L232 99 Q232 106 224 106 L22 106 Q16 106 16 100 Z" />
      <rect :fill="overlay" x="17" y="101" width="214" height="5" rx="2.5" />

      <!-- upper -->
      <path :fill="upper" d="M22 88 L22 50 Q22 38 34 36 L66 32 Q80 31 88 40 L116 64 Q126 72 148 74 L194 78 Q220 81 226 88 Z" />

      <!-- overlays: heel counter, toe cap, eyestay -->
      <path :fill="overlay" d="M22 88 L22 56 Q34 54 44 62 Q52 72 52 88 Z" />
      <path :fill="overlay" d="M170 76 L194 78 Q220 81 226 88 L166 88 Q164 82 170 76 Z" />
      <path :fill="overlay" d="M84 37 Q90 40 94 46 L118 66 Q124 71 132 73 L124 80 Q112 76 104 66 L78 42 Z" />
    </g>

    <!-- stitching -->
    <path d="M56 80 Q110 72 162 82" fill="none" stroke="currentColor" stroke-opacity="0.35" stroke-dasharray="3 3" />

    <!-- collar -->
    <path d="M26 40 Q46 34 70 34 Q80 34 86 39" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" />

    <!-- laces: dark outline under a lighter lace -->
    <g stroke-linecap="round">
      <path d="M90 44 L98 39 M98 51 L106 46 M106 58 L114 53 M114 65 L122 60" stroke="currentColor" stroke-width="5" />
      <path d="M90 44 L98 39 M98 51 L106 46 M106 58 L114 53 M114 65 L122 60" :stroke="sole" stroke-width="2.6" />
    </g>
  </svg>
</template>

<style scoped>
.shoe {
  width: 100%;
  height: auto;
  overflow: visible;
  color: var(--ink);
  transform: rotate(-4deg);
  transition: transform 0.35s cubic-bezier(0.34, 1.56, 0.64, 1);
}
</style>
