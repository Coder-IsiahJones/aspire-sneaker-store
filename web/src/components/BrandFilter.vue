<script setup lang="ts">
defineProps<{ brands: string[] }>()

const selected = defineModel<string | undefined>()
</script>

<template>
  <nav class="tabs" aria-label="Filter by brand">
    <button type="button" class="tab" :aria-pressed="!selected" @click="selected = undefined">
      All pairs
    </button>
    <button
      v-for="brand in brands"
      :key="brand"
      type="button"
      class="tab"
      :aria-pressed="selected === brand"
      @click="selected = brand"
    >
      {{ brand }}
    </button>
  </nav>
</template>

<style scoped>
.tabs {
  display: flex;
  gap: 4px;
  overflow-x: auto;
  border-bottom: 3px solid var(--ink);
  scrollbar-width: none;
}

.tab {
  flex: none;
  padding: 10px 18px 8px;
  border: 2px solid var(--ink);
  border-bottom: none;
  border-radius: 10px 10px 0 0;
  background: var(--kraft);
  color: var(--ink);
  font: 600 13px/1 var(--mono);
  letter-spacing: 0.06em;
  text-transform: uppercase;
  cursor: pointer;
  transform: translateY(4px);
  transition: transform 0.15s ease, background 0.15s ease;
}

.tab:hover {
  transform: translateY(1px);
}

.tab[aria-pressed='true'] {
  background: var(--ink);
  color: var(--paper);
  transform: translateY(0);
}
</style>
