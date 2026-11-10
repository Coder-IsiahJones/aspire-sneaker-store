<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { formatPrice, useCart } from '../composables/useCart'

const open = defineModel<boolean>({ required: true })
const { lines, count, total, remove } = useCart()

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') open.value = false
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Transition name="fade">
    <div v-if="open" class="scrim" @click="open = false" />
  </Transition>

  <Transition name="slide">
    <aside v-if="open" class="drawer" role="dialog" aria-modal="true" aria-label="Cart">
      <div class="receipt">
        <header class="receipt-head">
          <p class="store">STOCKROOM</p>
          <p class="meta">{{ count }} {{ count === 1 ? 'pair' : 'pairs' }}</p>
          <button type="button" class="close" aria-label="Close cart" @click="open = false">×</button>
        </header>

        <p v-if="lines.length === 0" class="empty">Nothing in the bag yet.</p>

        <ul v-else class="lines">
          <li v-for="line in lines" :key="line.key" class="line">
            <div>
              <p class="line-name">{{ line.sneaker.name }}</p>
              <p class="line-meta">
                {{ line.sneaker.colorway }} · US {{ line.size }} · x{{ line.quantity }}
              </p>
            </div>
            <div class="line-right">
              <span>{{ formatPrice(line.sneaker.price * line.quantity) }}</span>
              <button type="button" class="remove" @click="remove(line.key)">remove</button>
            </div>
          </li>
        </ul>

        <div class="total">
          <span>Total</span>
          <span>{{ formatPrice(total) }}</span>
        </div>

        <p class="note">Demo store. Checkout is not wired up.</p>
      </div>
    </aside>
  </Transition>
</template>

<style scoped>
.scrim {
  position: fixed;
  inset: 0;
  background: rgba(20, 20, 20, 0.45);
  z-index: 10;
}

.drawer {
  position: fixed;
  top: 0;
  right: 0;
  bottom: 0;
  width: min(380px, 100vw);
  padding: 24px 20px;
  overflow-y: auto;
  z-index: 11;
}

.receipt {
  position: relative;
  padding: 22px 20px 34px;
  background: var(--label);
  color: var(--ink);
  font: 400 13px/1.5 var(--mono);
  box-shadow: 0 18px 40px rgba(0, 0, 0, 0.3);
  /* torn paper edge along the bottom */
  mask:
    linear-gradient(#000 0 0) top / 100% calc(100% - 10px) no-repeat,
    conic-gradient(from -45deg at bottom, #0000, #000 1deg 90deg, #0000 91deg) bottom / 16px 10px repeat-x;
}

.receipt-head {
  display: grid;
  grid-template-columns: 1fr auto;
  padding-bottom: 14px;
  border-bottom: 1.5px dashed var(--ink);
  text-align: center;
}

.store {
  grid-column: 1 / -1;
  margin: 0;
  font: 900 34px/1 var(--display);
  letter-spacing: 0.04em;
}

.meta {
  grid-column: 1 / -1;
  margin: 4px 0 0;
  color: var(--muted);
  text-transform: uppercase;
  letter-spacing: 0.12em;
  font-size: 11px;
}

.close {
  position: absolute;
  top: 10px;
  right: 12px;
  border: none;
  background: none;
  color: var(--ink);
  font-size: 26px;
  line-height: 1;
  cursor: pointer;
}

.empty {
  margin: 28px 0;
  text-align: center;
  color: var(--muted);
}

.lines {
  margin: 0;
  padding: 0;
  list-style: none;
}

.line {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px dotted var(--muted);
}

.line p {
  margin: 0;
}

.line-name {
  font-weight: 600;
}

.line-meta {
  font-size: 11px;
  color: var(--muted);
}

.line-right {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
  white-space: nowrap;
}

.remove {
  padding: 0;
  border: none;
  background: none;
  color: var(--hot);
  font: inherit;
  font-size: 11px;
  text-decoration: underline;
  cursor: pointer;
}

.total {
  display: flex;
  justify-content: space-between;
  margin-top: 16px;
  padding-top: 12px;
  border-top: 3px double var(--ink);
  font: 900 26px/1 var(--display);
  text-transform: uppercase;
}

.note {
  margin: 18px 0 0;
  text-align: center;
  font-size: 11px;
  color: var(--muted);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

.slide-enter-active,
.slide-leave-active {
  transition: transform 0.3s cubic-bezier(0.22, 1, 0.36, 1);
}

.slide-enter-from,
.slide-leave-to {
  transform: translateX(105%);
}
</style>
