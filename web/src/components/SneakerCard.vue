<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Sneaker } from '../api'
import { formatPrice, useCart } from '../composables/useCart'
import ShoeArt from './ShoeArt.vue'

const props = defineProps<{ sneaker: Sneaker; index: number }>()

const cart = useCart()
const size = ref<number>()
const justAdded = ref(false)
const adding = ref(false)
const addFailed = ref(false)

// A fake barcode generated from the SKU, so every box label looks different.
const barcode = computed(() => {
  const bars: { x: number; width: number }[] = []
  let x = 0
  for (const char of props.sneaker.sku) {
    const code = char.charCodeAt(0)
    for (let bit = 0; bit < 3; bit++) {
      const width = ((code >> bit) & 1) + 1
      bars.push({ x, width })
      x += width + (((code >> (bit + 3)) & 1) + 1)
    }
  }
  return { bars, width: x }
})

function pickSize(option: number) {
  size.value = option
  addFailed.value = false
}

async function addToCart() {
  if (size.value === undefined || adding.value) return
  adding.value = true
  addFailed.value = false
  const added = await cart.add(props.sneaker, size.value)
  adding.value = false
  if (!added) {
    addFailed.value = true
    return
  }
  justAdded.value = true
  setTimeout(() => (justAdded.value = false), 1200)
}
</script>

<template>
  <article class="card" :style="{ '--i': index }">
    <div class="shelf">
      <span class="year">{{ sneaker.releaseYear }}</span>
      <ShoeArt :colors="sneaker.colors" />
    </div>

    <div class="label">
      <div class="label-row">
        <span class="brand">{{ sneaker.brand }}</span>
        <span class="sku">{{ sneaker.sku }}</span>
      </div>

      <h2 class="name">{{ sneaker.name }}</h2>
      <p class="colorway">"{{ sneaker.colorway }}"</p>
      <p class="description">{{ sneaker.description }}</p>

      <fieldset class="sizes">
        <legend>Size (US)</legend>
        <button
          v-for="option in sneaker.sizes"
          :key="option"
          type="button"
          class="size"
          :aria-pressed="size === option"
          @click="pickSize(option)"
        >
          {{ option }}
        </button>
      </fieldset>

      <div class="label-row label-foot">
        <svg class="barcode" :viewBox="`0 0 ${barcode.width} 20`" preserveAspectRatio="none" aria-hidden="true">
          <rect v-for="(bar, i) in barcode.bars" :key="i" :x="bar.x" :width="bar.width" height="20" />
        </svg>
        <span class="price">{{ formatPrice(sneaker.price) }}</span>
      </div>

      <button type="button" class="add" :disabled="size === undefined || adding" @click="addToCart">
        {{
          justAdded
            ? 'In the bag'
            : addFailed
              ? "Didn't save. Try again"
              : size === undefined
                ? 'Pick a size'
                : `Add size ${size}`
        }}
      </button>
    </div>
  </article>
</template>

<style scoped>
.card {
  display: flex;
  flex-direction: column;
  border: 2px solid var(--ink);
  border-radius: 6px;
  background: var(--kraft);
  box-shadow: 6px 6px 0 var(--ink);
  overflow: hidden;
  opacity: 0;
  animation: unbox 0.5s cubic-bezier(0.22, 1, 0.36, 1) forwards;
  animation-delay: calc(var(--i) * 60ms);
}

@keyframes unbox {
  from {
    opacity: 0;
    transform: translateY(24px) rotate(-1deg);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

.shelf {
  position: relative;
  padding: 28px 22px 14px;
  background:
    radial-gradient(circle at 30% 20%, rgba(255, 255, 255, 0.35), transparent 60%),
    var(--kraft);
  border-bottom: 2px dashed var(--kraft-dark);
}

.card:hover :deep(.shoe) {
  transform: rotate(-9deg) translate(-4px, -8px);
}

.year {
  position: absolute;
  top: 10px;
  left: 12px;
  font: 600 11px/1 var(--mono);
  letter-spacing: 0.12em;
  color: var(--kraft-dark);
}

.label {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 8px;
  margin: 14px;
  padding: 14px;
  border: 1.5px solid var(--ink);
  background: var(--label);
}

.label-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.brand,
.sku {
  font: 600 11px/1 var(--mono);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.sku {
  color: var(--muted);
}

.name {
  margin: 4px 0 0;
  font: 900 30px/0.95 var(--display);
  text-transform: uppercase;
}

.colorway {
  margin: 0;
  font: 500 13px/1.3 var(--mono);
  color: var(--hot);
}

.description {
  margin: 0;
  font: 400 12px/1.5 var(--mono);
  color: var(--muted);
}

.sizes {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(44px, 1fr));
  margin: 6px 0 0;
  padding: 0;
  border: none;
  border-top: 1.5px solid var(--ink);
  border-left: 1.5px solid var(--ink);
}

.sizes legend {
  padding: 0 0 6px;
  font: 600 10px/1 var(--mono);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.size {
  padding: 8px 0;
  border: none;
  border-right: 1.5px solid var(--ink);
  border-bottom: 1.5px solid var(--ink);
  background: transparent;
  color: var(--ink);
  font: 600 13px/1 var(--mono);
  cursor: pointer;
}

.size:hover {
  background: var(--paper);
}

.size[aria-pressed='true'] {
  background: var(--ink);
  color: var(--label);
}

.label-foot {
  margin-top: auto;
  padding-top: 6px;
}

.barcode {
  width: 110px;
  height: 30px;
  fill: var(--ink);
}

.price {
  padding: 6px 10px;
  border-radius: 999px;
  background: var(--hot);
  color: var(--ink);
  font: 900 22px/1 var(--display);
  transform: rotate(-4deg);
}

.add {
  padding: 12px;
  border: 2px solid var(--ink);
  background: var(--ink);
  color: var(--label);
  font: 600 13px/1 var(--mono);
  letter-spacing: 0.08em;
  text-transform: uppercase;
  cursor: pointer;
  transition: background 0.15s ease, color 0.15s ease;
}

.add:hover:not(:disabled) {
  background: var(--hot);
  color: var(--ink);
}

.add:disabled {
  background: transparent;
  color: var(--muted);
  border-style: dashed;
  cursor: not-allowed;
}
</style>
