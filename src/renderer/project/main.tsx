import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import './project.css';
import { installThemeStyle } from './theme';

// Before the first render: the colour tokens are generated from the shared palette
// (#77), and the preference that picks brand + appearance loads asynchronously. If
// this waited for applyTheme, the first frame would paint with no tokens at all.
installThemeStyle();

const container = document.getElementById('root');
if (!container) {
  throw new Error('Root element #root not found');
}

createRoot(container).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
