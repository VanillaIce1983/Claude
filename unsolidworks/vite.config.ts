import { defineConfig } from 'vite';
import { viteSingleFile } from 'vite-plugin-singlefile';

// One self-contained index.html: easy to open locally or host anywhere.
export default defineConfig({
  base: './',
  plugins: [viteSingleFile()],
  build: { target: 'es2020', chunkSizeWarningLimit: 2000 },
  test: { globals: true, include: ['tests/**/*.test.ts'] },
});
