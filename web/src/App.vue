<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { getBrands, getSneakers, type Sneaker } from './api'
import AccountDrawer from './components/AccountDrawer.vue'
import BrandFilter from './components/BrandFilter.vue'
import CartDrawer from './components/CartDrawer.vue'
import DropsDock from './components/DropsDock.vue'
import SneakerCard from './components/SneakerCard.vue'
import { useAccount } from './composables/useAccount'
import { useCart } from './composables/useCart'
import { useOrderEvents } from './composables/useOrderEvents'

const sneakers = ref<Sneaker[]>([])
const brands = ref<string[]>([])
const brand = ref<string>()
const loading = ref(true)
const error = ref<string>()
const cartOpen = ref(false)

const { count, load: loadBag } = useCart()
const { account, panelOpen, load: loadAccount } = useAccount()
useOrderEvents()

async function load() {
  loading.value = true
  error.value = undefined
  try {
    sneakers.value = await getSneakers(brand.value)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  void loadBag()
  void loadAccount()
  await load()
  brands.value = await getBrands().catch(() => [])
})

watch(brand, load)
</script>

<template>
  <div class="page">
    <header class="masthead">
      <div>
        <p class="eyebrow">Aisle 13 · Back room</p>
        <h1 class="wordmark">Stockroom</h1>
      </div>
      <p class="blurb">
        Twelve pairs, all deadstock. The catalog lives in MongoDB, a C# API serves it, and Aspire
        runs the whole thing.
      </p>
      <div class="tags">
        <button type="button" class="pass-tag" @click="panelOpen = true">
          {{ account ? 'My pass' : 'Sign in' }}
        </button>
        <button type="button" class="cart-tag" @click="cartOpen = true">
          Bag <span class="cart-count">{{ count }}</span>
        </button>
      </div>
    </header>

    <DropsDock />

    <BrandFilter v-model="brand" :brands="brands" />

    <main>
      <p v-if="loading" class="status">Pulling boxes from the back...</p>
      <div v-else-if="error" class="status status-error">
        <p>Couldn't reach the API.</p>
        <code>{{ error }}</code>
      </div>
      <section v-else class="grid">
        <SneakerCard v-for="(sneaker, i) in sneakers" :key="sneaker.id" :sneaker="sneaker" :index="i" />
      </section>
    </main>

    <CartDrawer v-model="cartOpen" />
    <AccountDrawer v-model="panelOpen" />
  </div>
</template>

<style scoped>
.page {
  max-width: 1240px;
  margin: 0 auto;
  padding: 32px 16px 80px;
}

.masthead {
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: end;
  gap: 32px;
  margin-bottom: 28px;
}

.eyebrow {
  margin: 0 0 6px;
  font: 600 12px/1 var(--mono);
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--kraft-dark);
}

.wordmark {
  margin: 0;
  font: 900 clamp(64px, 11vw, 132px) / 0.8 var(--display);
  letter-spacing: -0.01em;
  text-transform: uppercase;
  -webkit-text-stroke: 2px var(--ink);
  color: var(--hot);
  text-shadow: 6px 6px 0 var(--ink);
}

.blurb {
  max-width: 34ch;
  margin: 0;
  justify-self: end;
  color: var(--muted);
  font-size: 13px;
}

.tags {
  display: flex;
  align-items: center;
  gap: 16px;
}

.pass-tag {
  padding: 10px 14px 8px;
  border: 2px solid var(--ink);
  border-radius: 8px;
  background: var(--kraft);
  color: var(--ink);
  font: 900 18px/1 var(--display);
  text-transform: uppercase;
  cursor: pointer;
  box-shadow: 3px 3px 0 var(--ink);
  transform: rotate(-2deg);
  transition: transform 0.15s ease;
}

.pass-tag:hover {
  transform: rotate(0deg);
}

.cart-tag {
  position: relative;
  padding: 14px 22px 12px 34px;
  border: 2px solid var(--ink);
  border-radius: 4px 22px 22px 4px;
  background: var(--label);
  color: var(--ink);
  font: 900 22px/1 var(--display);
  text-transform: uppercase;
  cursor: pointer;
  box-shadow: 4px 4px 0 var(--ink);
  transform: rotate(3deg);
  transition: transform 0.15s ease;
}

/* the punched hole of a hang tag */
.cart-tag::before {
  content: '';
  position: absolute;
  left: 12px;
  top: 50%;
  width: 10px;
  height: 10px;
  margin-top: -5px;
  border: 2px solid var(--ink);
  border-radius: 50%;
  background: var(--paper);
}

.cart-tag:hover {
  transform: rotate(0deg);
}

.cart-count {
  display: inline-grid;
  place-items: center;
  min-width: 28px;
  height: 28px;
  margin-left: 6px;
  padding: 0 6px;
  border-radius: 999px;
  background: var(--hot);
  font-size: 18px;
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(270px, 1fr));
  gap: 28px;
  padding-top: 32px;
}

.status {
  padding: 64px 0;
  text-align: center;
  font: 900 32px/1.1 var(--display);
  text-transform: uppercase;
}

.status-error code {
  display: block;
  margin-top: 12px;
  font: 400 13px/1.4 var(--mono);
  color: var(--hot);
  text-transform: none;
}

@media (max-width: 760px) {
  .masthead {
    grid-template-columns: 1fr;
    gap: 16px;
  }

  .wordmark {
    font-size: 17vw;
    text-shadow: 4px 4px 0 var(--ink);
  }

  .blurb {
    justify-self: start;
  }

  /* on phones the bag becomes a hang tag pinned to the corner */
  .cart-tag {
    position: fixed;
    right: 16px;
    bottom: 20px;
    z-index: 5;
  }
}
</style>
