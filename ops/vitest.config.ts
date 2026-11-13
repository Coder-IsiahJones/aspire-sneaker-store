import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config.ts'

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'happy-dom',
      include: ['src/**/*.test.{ts,tsx}'],
      restoreMocks: true,
      unstubGlobals: true,
    },
  }),
)
