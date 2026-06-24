import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// https://vitejs.dev/config
// Single renderer build, two HTML entry points: index.html (project window)
// and toolbar.html (capture toolbar). Vite code-splits them so the small
// toolbar doesn't pull in the heavier project bundle.
export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      input: {
        main_window: path.resolve(process.cwd(), 'index.html'),
        toolbar: path.resolve(process.cwd(), 'toolbar.html'),
      },
    },
  },
});
