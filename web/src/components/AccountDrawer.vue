<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ApiError, type Sneaker } from '../api'
import { useAccount } from '../composables/useAccount'
import { formatPrice, useCart } from '../composables/useCart'
import ShoeArt from './ShoeArt.vue'

const open = defineModel<boolean>({ required: true })
const { account, wishlist, orders, panelTab, register, signIn, signOut, toggleWishlist } = useAccount()
const cart = useCart()

const panel = ref<HTMLElement>()
const mode = ref<'sign-in' | 'register'>('sign-in')
const email = ref('')
const password = ref('')
const busy = ref(false)
const formError = ref<string>()
const bagged = ref<string>()
const bagFailed = ref<string>()

let opener: HTMLElement | null = null

function switchMode() {
  mode.value = mode.value === 'sign-in' ? 'register' : 'sign-in'
  formError.value = undefined
}

function messageFor(error: unknown) {
  const status = error instanceof ApiError ? error.status : 0
  if (status === 401) return "That email and password don't match."
  if (status === 409) return 'That email already has an account.'
  if (status === 400) return 'Use a valid email and a password of at least 8 characters.'
  if (status === 429) return 'Too many tries. Wait a minute and try again.'
  return "Couldn't reach the store. Try again."
}

async function submit() {
  if (busy.value) return
  busy.value = true
  formError.value = undefined
  try {
    await (mode.value === 'sign-in' ? signIn : register)(email.value, password.value)
    email.value = ''
    password.value = ''
  } catch (error) {
    formError.value = messageFor(error)
  } finally {
    busy.value = false
  }
}

async function leave() {
  try {
    await signOut()
  } catch {
    formError.value = "Couldn't reach the store. Try again."
  }
}

async function bag(sneaker: Sneaker, size: number) {
  const key = `${sneaker.id}:${size}`
  bagFailed.value = undefined
  if (await cart.add(sneaker, size)) {
    bagged.value = key
    setTimeout(() => {
      if (bagged.value === key) bagged.value = undefined
    }, 1200)
  } else {
    bagFailed.value = sneaker.id
  }
}

const placedOn = (iso: string) =>
  new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso))

const orderNumber = (id: string) => id.slice(-6).toUpperCase()

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') open.value = false
}

// Move focus into the drawer when it opens, and back to what opened it when it closes.
watch(open, async (isOpen) => {
  if (isOpen) {
    opener = document.activeElement as HTMLElement | null
    await nextTick()
    panel.value?.querySelector<HTMLElement>('input, .tab')?.focus()
  } else {
    opener?.focus?.()
  }
})

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Transition name="fade">
    <div v-if="open" class="scrim" @click="open = false" />
  </Transition>

  <Transition name="slide">
    <aside v-if="open" ref="panel" class="drawer" role="dialog" aria-modal="true" aria-label="Account">
      <button type="button" class="close" aria-label="Close account" @click="open = false">×</button>

      <section v-if="!account" class="pass">
        <p class="eyebrow">Back room pass</p>
        <h2 class="title">{{ mode === 'sign-in' ? 'Sign in' : 'Get a pass' }}</h2>
        <p class="pitch">Your bag on every device, a saved list, and receipts for everything you buy.</p>

        <form class="form" @submit.prevent="submit">
          <label class="field">
            <span>Email</span>
            <input v-model="email" type="email" name="email" autocomplete="username" required />
          </label>
          <label class="field">
            <span>Password</span>
            <input
              v-model="password"
              type="password"
              name="password"
              :autocomplete="mode === 'sign-in' ? 'current-password' : 'new-password'"
              :minlength="mode === 'register' ? 8 : undefined"
              required
            />
          </label>
          <p v-if="formError" class="error" role="alert">{{ formError }}</p>
          <button type="submit" class="go" :disabled="busy">
            {{ busy ? 'Checking the list...' : mode === 'sign-in' ? 'Sign in' : 'Create pass' }}
          </button>
        </form>

        <button type="button" class="switch" @click="switchMode">
          {{ mode === 'sign-in' ? 'New here? Get a pass' : 'Already have a pass? Sign in' }}
        </button>
      </section>

      <template v-else>
        <section class="pass pass-issued">
          <p class="eyebrow">Back room pass</p>
          <p class="holder">{{ account.email }}</p>
          <div class="pass-foot">
            <span class="stripes" aria-hidden="true" />
            <button type="button" class="switch" @click="leave">Sign out</button>
          </div>
          <p v-if="formError" class="error" role="alert">{{ formError }}</p>
        </section>

        <div class="tabs" role="tablist" aria-label="Your stuff">
          <button
            type="button"
            class="tab"
            role="tab"
            :aria-selected="panelTab === 'orders'"
            @click="panelTab = 'orders'"
          >
            Orders <span class="count">{{ orders.length }}</span>
          </button>
          <button
            type="button"
            class="tab"
            role="tab"
            :aria-selected="panelTab === 'saved'"
            @click="panelTab = 'saved'"
          >
            Saved <span class="count">{{ wishlist.length }}</span>
          </button>
        </div>

        <div v-if="panelTab === 'orders'" role="tabpanel">
          <p v-if="orders.length === 0" class="empty">No receipts yet.</p>
          <article v-for="order in orders" :key="order.id" class="receipt">
            <header class="receipt-head">
              <span>No. {{ orderNumber(order.id) }}</span>
              <span>{{ placedOn(order.placedAt) }}</span>
            </header>
            <ul class="lines">
              <li v-for="line in order.lines" :key="`${line.sneakerId}:${line.size}`" class="line">
                <div>
                  <p class="line-name">{{ line.name }}</p>
                  <p class="line-meta">{{ line.colorway }} · US {{ line.size }} · x{{ line.quantity }}</p>
                </div>
                <span>{{ formatPrice(line.unitPrice * line.quantity) }}</span>
              </li>
            </ul>
            <div class="total">
              <span>Total</span>
              <span>{{ formatPrice(order.total) }}</span>
            </div>
          </article>
        </div>

        <div v-else role="tabpanel">
          <p v-if="wishlist.length === 0" class="empty">Nothing saved yet. Tag a box to keep it here.</p>
          <article v-for="sneaker in wishlist" :key="sneaker.id" class="saved">
            <div class="saved-art"><ShoeArt :colors="sneaker.colors" /></div>
            <div class="saved-body">
              <p class="line-name">{{ sneaker.name }}</p>
              <p class="line-meta">{{ sneaker.colorway }} · {{ formatPrice(sneaker.price) }}</p>
              <div class="sizes" role="group" :aria-label="`Add ${sneaker.name} to the bag`">
                <button
                  v-for="size in sneaker.sizes"
                  :key="size"
                  type="button"
                  class="size"
                  @click="bag(sneaker, size)"
                >
                  {{ bagged === `${sneaker.id}:${size}` ? '✓' : size }}
                </button>
              </div>
              <p v-if="bagFailed === sneaker.id" class="error" role="alert">Didn't save. Try again.</p>
            </div>
            <button
              type="button"
              class="unsave"
              :aria-label="`Remove ${sneaker.name} from saved`"
              @click="toggleWishlist(sneaker)"
            >
              ×
            </button>
          </article>
        </div>
      </template>
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
  width: min(400px, 100vw);
  padding: 24px 20px 40px;
  background: var(--ink);
  color: var(--label);
  overflow-y: auto;
  z-index: 11;
}

.close {
  position: absolute;
  z-index: 1;
  top: 8px;
  right: 10px;
  border: none;
  background: none;
  color: var(--label);
  font-size: 26px;
  line-height: 1;
  cursor: pointer;
}

/* a laminated staff pass, hung from a punched slot */
.pass {
  position: relative;
  margin-top: 14px;
  padding: 34px 20px 20px;
  border-radius: 12px;
  background: var(--kraft);
  color: var(--ink);
  box-shadow: 5px 5px 0 var(--hot);
}

.pass::before {
  content: '';
  position: absolute;
  top: 12px;
  left: 50%;
  width: 52px;
  height: 10px;
  margin-left: -26px;
  border-radius: 999px;
  background: var(--ink);
}

.eyebrow {
  margin: 0 0 6px;
  font: 600 11px/1 var(--mono);
  letter-spacing: 0.18em;
  text-transform: uppercase;
}

.title {
  margin: 0;
  font: 900 56px/0.9 var(--display);
  text-transform: uppercase;
}

.pitch {
  margin: 10px 0 18px;
  font-size: 12px;
  line-height: 1.5;
}

.holder {
  margin: 0;
  font: 900 26px/1.05 var(--display);
  overflow-wrap: anywhere;
}

.pass-foot {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 16px;
  margin-top: 16px;
}

.stripes {
  flex: 1;
  max-width: 150px;
  height: 26px;
  background: repeating-linear-gradient(90deg, var(--ink) 0 2px, transparent 2px 4px, var(--ink) 4px 7px, transparent 7px 9px);
}

.form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.field span {
  display: block;
  margin-bottom: 4px;
  font: 600 10px/1 var(--mono);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.field input {
  width: 100%;
  padding: 10px;
  border: 2px solid var(--ink);
  border-radius: 0;
  background: var(--label);
  color: var(--ink);
  font: 500 14px/1.2 var(--mono);
}

.field input:focus-visible {
  outline: 3px solid var(--hot);
  outline-offset: 1px;
}

.error {
  margin: 0;
  padding: 6px 8px;
  background: var(--ink);
  color: var(--label);
  font-size: 12px;
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

.go:hover:not(:disabled) {
  background: var(--hot);
  color: var(--ink);
}

.go:disabled {
  cursor: progress;
  opacity: 0.7;
}

.switch {
  margin-top: 12px;
  padding: 0;
  border: none;
  background: none;
  color: var(--ink);
  font: 500 12px/1.2 var(--mono);
  text-decoration: underline;
  cursor: pointer;
}

.pass-foot .switch {
  margin: 0;
}

.tabs {
  display: flex;
  gap: 4px;
  margin-top: 26px;
  border-bottom: 3px solid var(--label);
}

.tab {
  flex: 1;
  padding: 10px 0 8px;
  border: 2px solid var(--label);
  border-bottom: none;
  border-radius: 10px 10px 0 0;
  background: transparent;
  color: var(--label);
  font: 600 13px/1 var(--mono);
  letter-spacing: 0.06em;
  text-transform: uppercase;
  cursor: pointer;
}

.tab[aria-selected='true'] {
  background: var(--label);
  color: var(--ink);
}

.count {
  margin-left: 4px;
  color: var(--hot);
}

.empty {
  margin: 28px 0;
  text-align: center;
  color: var(--kraft);
  font-size: 13px;
}

.receipt {
  margin-top: 16px;
  padding: 14px 16px 24px;
  background: var(--label);
  color: var(--ink);
  font: 400 12px/1.5 var(--mono);
  /* torn paper edge along the bottom */
  mask:
    linear-gradient(#000 0 0) top / 100% calc(100% - 8px) no-repeat,
    conic-gradient(from -45deg at bottom, #0000, #000 1deg 90deg, #0000 91deg) bottom / 14px 8px repeat-x;
}

.receipt-head {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding-bottom: 8px;
  border-bottom: 1.5px dashed var(--ink);
  font-size: 11px;
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
  padding: 8px 0;
  border-bottom: 1px dotted var(--muted);
}

.line p,
.saved p {
  margin: 0;
}

.line-name {
  font-weight: 600;
}

.line-meta {
  font-size: 11px;
  color: var(--muted);
}

.total {
  display: flex;
  justify-content: space-between;
  margin-top: 10px;
  font: 900 22px/1 var(--display);
  text-transform: uppercase;
}

.saved {
  position: relative;
  display: flex;
  gap: 12px;
  margin-top: 14px;
  padding: 12px;
  background: var(--label);
  color: var(--ink);
  font: 400 12px/1.5 var(--mono);
}

.saved-art {
  flex: none;
  width: 76px;
  padding-top: 6px;
}

.saved-body {
  flex: 1;
  min-width: 0;
  padding-right: 14px;
}

.sizes {
  display: flex;
  flex-wrap: wrap;
  gap: 3px;
  margin-top: 8px;
}

.size {
  min-width: 36px;
  padding: 6px 4px;
  border: 1.5px solid var(--ink);
  background: transparent;
  color: var(--ink);
  font: 600 11px/1 var(--mono);
  cursor: pointer;
}

.size:hover {
  background: var(--ink);
  color: var(--label);
}

.unsave {
  position: absolute;
  top: 4px;
  right: 6px;
  border: none;
  background: none;
  color: var(--muted);
  font-size: 20px;
  line-height: 1;
  cursor: pointer;
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
