// The shotAI application menu. File → Settings signals the renderer to open the
// Settings view; Help → About shows app/runtime info. Standard Edit/View/Window
// submenus are kept (via roles) so copy/paste, devtools, and zoom still work.
import {
  app,
  Menu,
  dialog,
  nativeImage,
  BrowserWindow,
  type MenuItemConstructorOptions,
} from 'electron';
import { IpcChannels } from '../shared/ipc';
import {
  BRANDS,
  BRAND_IDS,
  DEFAULT_BRAND,
  type BrandId,
} from '../shared/theme-palette';
import { appIconPath } from './paths';

/**
 * What View -> Brand should currently show.
 *
 * The control is PER PROJECT but the application menu is global, so the renderer
 * pushes this whenever the open project or its brand changes and the menu is
 * rebuilt from it. Nothing here is derived main-side: main does not know which
 * project the renderer has open, and inventing a second source for that is how
 * a menu ends up describing a project the user already closed.
 */
export interface BrandMenuState {
  /** Whether a project is open. The submenu is disabled when it is not. */
  projectOpen: boolean;
  /** The open project's PINNED brand, or null when it follows the app setting. */
  projectTheme: BrandId | null;
  /** The app-level setting, so "App default" can say what it resolves to. */
  appBrand: BrandId;
}

let brandState: BrandMenuState = {
  projectOpen: false,
  projectTheme: null,
  appBrand: DEFAULT_BRAND,
};

/** Rebuilds the menu from the current state; set by installAppMenu. */
let rebuildMenu: (() => void) | null = null;

/**
 * Update View -> Brand.
 *
 * Rebuilds the whole menu rather than mutating MenuItem.checked: on Windows the
 * menu bar is owned by the native window and item mutation is only reliably
 * reflected after a rebuild, and a radio group especially needs all its items
 * set together or two can read as checked at once.
 *
 * Rebuilding is skipped when nothing changed. The renderer pushes this from an
 * effect that also runs for unrelated re-renders, and rebuilding the application
 * menu under the user's cursor closes an open menu.
 */
export function setBrandMenuState(next: BrandMenuState): void {
  if (
    next.projectOpen === brandState.projectOpen &&
    next.projectTheme === brandState.projectTheme &&
    next.appBrand === brandState.appBrand
  ) {
    return;
  }
  brandState = next;
  rebuildMenu?.();
}

/** Build + install the application menu. `getProjectWindow` returns the main
 *  project window — the target for File → Settings and the About dialog parent. */
export function installAppMenu(getProjectWindow: () => BrowserWindow | null): void {
  const isMac = process.platform === 'darwin';

  const showAbout = (): void => {
    const win = getProjectWindow();
    const icon = nativeImage.createFromPath(appIconPath()).resize({ width: 64, height: 64 });
    const options: Electron.MessageBoxOptions = {
      type: 'info',
      title: 'About shotAI',
      message: `${app.getName()} ${app.getVersion()}`,
      detail:
        'Local-first SOP builder — capture a process and let Claude write the guide.\n\n' +
        `Electron ${process.versions.electron} · Chromium ${process.versions.chrome}\n` +
        `${process.platform}/${process.arch}`,
      buttons: ['OK'],
      ...(icon.isEmpty() ? {} : { icon }),
    };
    if (win) void dialog.showMessageBox(win, options);
    else void dialog.showMessageBox(options);
  };

  /**
   * The Brand radio group.
   *
   * TWO kinds of entry, and the difference is real: "App default" writes NO key
   * to project.json and keeps following the app setting, while a named brand
   * pins that brand into the file so the project reproduces identically on any
   * machine. The resolved brand is shown in the default's label so the menu
   * still says what the project will actually look like.
   *
   * The DEFAULT brand is deliberately not offered as a named entry. The
   * cross-platform write rule stores it as an absent key, so picking it would be
   * indistinguishable from "App default" — a control that silently snaps back to
   * the option above it. See the note on ProjectManifest.theme.
   */
  const brandItems = (): MenuItemConstructorOptions[] => {
    const choose = (brand: BrandId | null): void =>
      getProjectWindow()?.webContents.send(IpcChannels.menuSetProjectTheme, brand);
    return [
      {
        label: `App default (${BRANDS[brandState.appBrand].label})`,
        type: 'radio',
        checked: brandState.projectTheme === null,
        click: () => choose(null),
      },
      ...BRAND_IDS.filter((id) => id !== DEFAULT_BRAND).map(
        (id): MenuItemConstructorOptions => ({
          label: BRANDS[id].label,
          type: 'radio',
          checked: brandState.projectTheme === id,
          click: () => choose(id),
        }),
      ),
    ];
  };

  const template: MenuItemConstructorOptions[] = [
    ...(isMac ? [{ role: 'appMenu' as const }] : []),
    {
      label: 'File',
      submenu: [
        {
          label: 'Import Project…',
          accelerator: 'CmdOrCtrl+O',
          click: () => getProjectWindow()?.webContents.send(IpcChannels.menuImportProject),
        },
        {
          label: 'Settings',
          accelerator: 'CmdOrCtrl+,',
          click: () => getProjectWindow()?.webContents.send(IpcChannels.openSettings),
        },
        { type: 'separator' },
        isMac ? { role: 'close' } : { role: 'quit' },
      ],
    },
    { role: 'editMenu' },
    {
      // Spelled out rather than `role: 'viewMenu'` so Brand can join it. The
      // standard entries keep their roles, so reload/zoom/fullscreen and their
      // accelerators behave exactly as the role menu provided.
      label: 'View',
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        { role: 'toggleDevTools' },
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        { role: 'togglefullscreen' },
        { type: 'separator' },
        {
          label: 'Brand',
          // Per project, so there is nothing to set without one open. Disabling
          // the parent greys the whole submenu rather than opening it onto a
          // list of dead radio buttons.
          enabled: brandState.projectOpen,
          submenu: brandItems(),
        },
      ],
    },
    { role: 'windowMenu' },
    {
      role: 'help',
      submenu: [{ label: 'About shotAI', click: showAbout }],
    },
  ];

  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

/**
 * Rebuild the menu from the current brand state.
 *
 * installAppMenu builds its template from `brandState` at call time, so
 * re-running it IS the rebuild. Registered here rather than exported so the only
 * way to refresh the menu is through setBrandMenuState, which holds the
 * changed-check.
 */
export function armBrandMenu(getProjectWindow: () => BrowserWindow | null): void {
  rebuildMenu = () => installAppMenu(getProjectWindow);
}
