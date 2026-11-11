<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ remainingMs: number; label: string }>()

const units = computed(() => {
  const total = Math.max(0, Math.ceil(props.remainingMs / 1000))
  const days = Math.floor(total / 86400)
  const parts = [
    { name: 'hr', value: Math.floor((total % 86400) / 3600) },
    { name: 'min', value: Math.floor((total % 3600) / 60) },
    { name: 'sec', value: total % 60 },
  ]
  if (days > 0) parts.unshift({ name: 'day', value: days })
  return parts.map(({ name, value }) => ({
    name,
    digits: String(value).padStart(2, '0').split(''),
  }))
})
</script>

<template>
  <div class="countdown" role="timer" :aria-label="label">
    <p class="caption">{{ label }}</p>
    <div class="units">
      <div v-for="unit in units" :key="unit.name" class="unit">
        <div class="flaps">
          <!-- keyed on the digit, so each change remounts it and replays the flip -->
          <span v-for="(digit, i) in unit.digits" :key="`${i}-${digit}`" class="flap">{{ digit }}</span>
        </div>
        <span class="unit-name">{{ unit.name }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.caption {
  margin: 0 0 6px;
  font: 600 10px/1 var(--mono);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.units {
  display: flex;
  gap: 8px;
}

.flaps {
  display: flex;
  gap: 2px;
  perspective: 200px;
}

.flap {
  position: relative;
  width: 26px;
  padding: 7px 0 5px;
  border-radius: 3px;
  background: var(--ink);
  color: var(--label);
  font: 900 30px/1 var(--display);
  font-variant-numeric: tabular-nums;
  text-align: center;
  transform-origin: 50% 50%;
  animation: flip 0.28s ease-out;
}

/* the hinge line across the middle of a split-flap digit */
.flap::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  top: 50%;
  height: 1px;
  background: rgba(251, 250, 246, 0.35);
}

@keyframes flip {
  from {
    transform: rotateX(-80deg);
    opacity: 0.4;
  }
  to {
    transform: none;
    opacity: 1;
  }
}

.unit-name {
  display: block;
  margin-top: 4px;
  font: 600 9px/1 var(--mono);
  letter-spacing: 0.12em;
  text-align: center;
  text-transform: uppercase;
  color: var(--muted);
}
</style>
