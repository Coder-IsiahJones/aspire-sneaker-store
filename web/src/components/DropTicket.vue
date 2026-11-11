<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Drop } from '../api'
import { formatPrice, useCart } from '../composables/useCart'
import DropCountdown from './DropCountdown.vue'
import ShoeArt from './ShoeArt.vue'

const props = defineProps<{ drop: Drop; now: number; entering: boolean; failed: boolean }>()
const emit = defineEmits<{ enter: [size: number] }>()

const cart = useCart()
const size = ref<number>()
const claiming = ref(false)
const claimFailed = ref(false)

const result = computed(() => props.drop.entry?.result)
const won = computed(() => props.drop.phase === 'closed' && result.value === 'won')
const lost = computed(() => props.drop.phase === 'closed' && result.value === 'lost')

// How far the roller door is pulled down over the shoe.
const shutter = computed(() => {
  if (props.drop.phase === 'upcoming') return '84%'
  if (props.drop.phase === 'open' || won.value) return '7%'
  return '100%'
})

const tag = computed(() =>
  props.drop.phase === 'open' ? 'Live' : props.drop.phase === 'upcoming' ? 'Soon' : 'Drawn',
)

const stamp = computed(() => {
  if (won.value) return 'Winner'
  if (lost.value) return 'No pair'
  return props.drop.phase === 'closed' ? 'Closed' : undefined
})

const countdown = computed(() => {
  if (props.drop.phase === 'upcoming') {
    return { label: 'Entries open in', target: Date.parse(props.drop.opensAt) }
  }
  if (props.drop.phase === 'open') {
    return { label: 'Draw in', target: Date.parse(props.drop.closesAt) }
  }
  return undefined
})

const opensLabel = computed(() =>
  new Intl.DateTimeFormat('en-US', {
    weekday: 'short',
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(props.drop.opensAt)),
)

const pairsLabel = computed(() => `${props.drop.pairs} ${props.drop.pairs === 1 ? 'pair' : 'pairs'}`)

const inBag = computed(() => {
  const entry = props.drop.entry
  return !!entry && cart.lines.some((line) => line.key === `${props.drop.sneaker.id}:${entry.size}`)
})

function submit() {
  if (size.value !== undefined) emit('enter', size.value)
}

async function claim() {
  if (!props.drop.entry || claiming.value) return
  claiming.value = true
  claimFailed.value = !(await cart.add(props.drop.sneaker, props.drop.entry.size))
  claiming.value = false
}
</script>

<template>
  <article class="ticket" :data-phase="drop.phase">
    <div class="bay">
      <span class="tag">{{ tag }}</span>
      <ShoeArt :colors="drop.sneaker.colors" />
      <div class="shutter" :style="{ height: shutter }" aria-hidden="true" />
      <span v-if="stamp" :key="stamp" class="stamp" :class="{ 'stamp-won': won }">{{ stamp }}</span>
    </div>

    <div class="slip">
      <div class="row">
        <span class="small">{{ drop.sneaker.brand }}</span>
        <span class="small faint">{{ drop.sneaker.sku }}</span>
      </div>

      <h3 class="name">{{ drop.sneaker.name }}</h3>
      <p class="colorway">"{{ drop.sneaker.colorway }}"</p>

      <dl class="facts">
        <div>
          <dt>Pairs</dt>
          <dd>{{ drop.pairs }}</dd>
        </div>
        <div>
          <dt>Entered</dt>
          <dd>{{ drop.entries }}</dd>
        </div>
        <div>
          <dt>Price</dt>
          <dd>{{ formatPrice(drop.sneaker.price) }}</dd>
        </div>
      </dl>

      <DropCountdown v-if="countdown" :remaining-ms="countdown.target - now" :label="countdown.label" />

      <div class="action">
        <p v-if="drop.phase === 'upcoming'" class="note">Entries open {{ opensLabel }}.</p>

        <template v-else-if="drop.phase === 'open' && !drop.entry">
          <fieldset class="sizes">
            <legend>Size (US)</legend>
            <button
              v-for="option in drop.sneaker.sizes"
              :key="option"
              type="button"
              class="size"
              :aria-pressed="size === option"
              @click="size = option"
            >
              {{ option }}
            </button>
          </fieldset>
          <button type="button" class="go" :disabled="size === undefined || entering" @click="submit">
            {{ entering ? 'Tearing ticket...' : size === undefined ? 'Pick a size' : `Enter for size ${size}` }}
          </button>
          <p v-if="failed" class="note note-error" role="alert">That didn't go through. Try again.</p>
        </template>

        <p v-else-if="drop.phase === 'open'" class="note" role="status">
          <strong>You're in, size {{ drop.entry?.size }}.</strong> Come back when the timer hits zero.
        </p>

        <template v-else-if="won">
          <p class="note" role="status"><strong>You won a pair, size {{ drop.entry?.size }}.</strong></p>
          <button type="button" class="go go-hot" :disabled="inBag || claiming" @click="claim">
            {{ inBag ? 'In the bag' : `Bag it for ${formatPrice(drop.sneaker.price)}` }}
          </button>
          <p v-if="claimFailed" class="note note-error" role="alert">That didn't go through. Try again.</p>
        </template>

        <p v-else-if="lost" class="note" role="status">
          Not this time. {{ pairsLabel }}, {{ drop.entries }} entered.
        </p>

        <p v-else class="note">Entries closed. {{ pairsLabel }}, {{ drop.entries }} entered.</p>
      </div>
    </div>
  </article>
</template>

<style scoped>
.ticket {
  display: flex;
  flex-direction: column;
  border-radius: 8px;
  background: var(--label);
  /* the dock around it sets light text */
  color: var(--ink);
  box-shadow: 6px 6px 0 var(--kraft-dark);
  overflow: hidden;
  transition: box-shadow 0.3s ease;
}

.ticket[data-phase='open'] {
  box-shadow: 6px 6px 0 var(--hot);
}

.ticket[data-phase='closed'] {
  box-shadow: 6px 6px 0 var(--muted);
}

.bay {
  position: relative;
  padding: 30px 44px 16px;
  background: var(--kraft);
  overflow: hidden;
}

.shutter {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  background: repeating-linear-gradient(#45423c 0 11px, #2a2824 11px 13px);
  /* the door's bottom rail, striped like a warning bar */
  border-bottom: 7px solid transparent;
  border-image: repeating-linear-gradient(-45deg, var(--hot) 0 9px, var(--ink) 9px 18px) 7;
  transition: height 1.1s cubic-bezier(0.5, 0, 0.2, 1);
}

.tag {
  position: absolute;
  z-index: 2;
  top: 10px;
  left: 10px;
  padding: 4px 8px 3px;
  background: var(--ink);
  color: var(--label);
  font: 600 10px/1 var(--mono);
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

[data-phase='open'] .tag {
  background: var(--hot);
  color: var(--ink);
  animation: pulse 1.4s ease-in-out infinite;
}

@keyframes pulse {
  50% {
    opacity: 0.55;
  }
}

.stamp {
  position: absolute;
  z-index: 2;
  top: 50%;
  left: 50%;
  padding: 6px 14px 4px;
  border: 4px double var(--label);
  color: var(--label);
  font: 900 40px/1 var(--display);
  text-transform: uppercase;
  white-space: nowrap;
  transform: translate(-50%, -50%) rotate(-11deg);
  animation: thunk 0.32s cubic-bezier(0.3, 1.6, 0.6, 1) 0.9s backwards;
}

.stamp-won {
  border-color: var(--ink);
  background: var(--hot);
  color: var(--ink);
}

@keyframes thunk {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) rotate(-3deg) scale(2);
  }
}

.slip {
  position: relative;
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 10px;
  padding: 18px 18px 18px;
  border-top: 2px dashed var(--muted);
}

/* the notches punched either end of the perforation */
.slip::before,
.slip::after {
  content: '';
  position: absolute;
  top: -11px;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: var(--ink);
}

.slip::before {
  left: -10px;
}

.slip::after {
  right: -10px;
}

.row {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}

.small {
  font: 600 11px/1 var(--mono);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.faint {
  color: var(--muted);
}

.name {
  margin: 0;
  font: 900 32px/0.95 var(--display);
  text-transform: uppercase;
}

.colorway {
  margin: -6px 0 0;
  font: 500 13px/1.3 var(--mono);
  color: var(--hot);
}

.facts {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  margin: 0;
  border-top: 1.5px solid var(--ink);
  border-bottom: 1.5px solid var(--ink);
}

.facts div {
  padding: 7px 0 6px;
}

.facts div + div {
  padding-left: 10px;
  border-left: 1.5px solid var(--ink);
}

.facts dt {
  font: 600 9px/1 var(--mono);
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--muted);
}

.facts dd {
  margin: 3px 0 0;
  font: 900 22px/1 var(--display);
}

.action {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: auto;
}

.note {
  margin: 0;
  font: 400 12px/1.5 var(--mono);
  color: var(--muted);
}

.note strong {
  color: var(--ink);
}

.note-error {
  color: var(--hot);
}

.sizes {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(44px, 1fr));
  margin: 0;
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

.go {
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

.go-hot,
.go:hover:not(:disabled) {
  background: var(--hot);
  color: var(--ink);
}

.go:disabled {
  background: transparent;
  color: var(--muted);
  border-style: dashed;
  cursor: not-allowed;
}
</style>
