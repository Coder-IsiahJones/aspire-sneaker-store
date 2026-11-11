<script setup lang="ts">
import { useDrops } from '../composables/useDrops'
import DropTicket from './DropTicket.vue'

const { drops, now, entering, failed, enter } = useDrops()
</script>

<template>
  <section v-if="drops.length" class="dock" aria-labelledby="dock-title">
    <header class="dock-head">
      <h2 id="dock-title" class="dock-title">Loading dock</h2>
      <p class="dock-blurb">
        Limited pairs that never reach the shelves. One entry each, winners drawn at random when the
        timer hits zero.
      </p>
    </header>

    <div class="tickets">
      <DropTicket
        v-for="drop in drops"
        :key="drop.id"
        :drop="drop"
        :now="now"
        :entering="entering === drop.id"
        :failed="failed === drop.id"
        @enter="enter(drop.id, $event)"
      />
    </div>
  </section>
</template>

<style scoped>
.dock {
  margin-bottom: 36px;
  padding: 34px 24px 34px;
  border-radius: 6px;
  background:
    repeating-linear-gradient(-45deg, var(--hot) 0 16px, var(--ink) 16px 32px) top / 100% 12px no-repeat,
    var(--ink);
  color: var(--label);
}

.dock-head {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  justify-content: space-between;
  gap: 8px 32px;
  margin-bottom: 22px;
}

.dock-title {
  margin: 0;
  font: 900 clamp(40px, 6vw, 64px) / 0.9 var(--display);
  text-transform: uppercase;
}

.dock-blurb {
  max-width: 46ch;
  margin: 0;
  font-size: 13px;
  color: var(--kraft);
}

.tickets {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 290px), 1fr));
  gap: 26px;
  align-items: stretch;
}

@media (max-width: 760px) {
  .dock {
    padding: 30px 14px 26px;
  }
}
</style>
