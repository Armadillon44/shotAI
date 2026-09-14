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
import { mainLog as menuLog } from './logger';

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

/** Rebuilds the menu from the current state; set by armBrandMenu. */
let rebuildMenu: (() => void) | null = null;

/** Pending deferred rebuild, so a burst of pushes collapses into one. */
let rebuildTimer: NodeJS.Timeout | null = null;

/**
 * Rebuild, but never on the stack that asked for it.
 *
 * Replacing the application menu while the native menu is still tearing down
 * after a click is a known way to lose the window on Windows, and a rebuild
 * triggered by a menu ITEM lands inside exactly that window: click -> IPC ->
 * store write -> state push -> rebuild, the whole round trip taking tens of
 * milliseconds. Deferring moves it clear of the teardown and coalesces a burst
 * of pushes into one rebuild.
 *
 * Wrapped, because a throw here would reject the IPC call that pushed the state
 * and tell the renderer its menu update failed, when the menu is cosmetic and
 * the setting it describes is already saved.
 */
function scheduleRebuild(): void {
  if (rebuildTimer) clearTimeout(rebuildTimer);
  rebuildTimer = setTimeout(() => {
    rebuildTimer = null;
    try {
      rebuildMenu?.();
    } catch (err) {
      menuLog.warn('brand menu rebuild failed (non-fatal):', err);
    }
  }, REBUILD_DEFER_MS);
}

/**
 * How long to wait. Long enough to be clear of the native menu's teardown,
 * short enough that nobody sees the menu lag behind the project they opened.
 */
const REBUILD_DEFER_MS = 120;

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
  scheduleRebuild();
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
   * PINS that brand into the file so the project reproduces identically on any
   * machine. The resolved brand is shown in the default's label so the menu
   * still says what the project will actually look like.
   *
   * EVERY brand is offered, the default included. An earlier version left the
   * default out, reasoning that the write rule stored it as an absent key and
   * so picking it would be indistinguishable from "App default". That was a
   * real bug rather than a tidy simplification: with the APP brand set to LFI,
   * "App default" and "LFI" both render LFI and shotAI was not on the menu at
   * all, so every option produced the same document and the control looked
   * broken. The write rule was corrected instead — see ProjectManifest.theme.
   */
  const brandItems = (): MenuItemConstructorOptions[] => {
    const choose = (brand: BrandId | null): void => {
      // Record it HERE as well as sending it. Electron has already moved the
      // radio dot natively, and the renderer will echo this same value back
      // once the write lands — which then hits the changed-check and rebuilds
      // NOTHING. That is the point: it keeps the rebuild out of the click path
      // entirely, rather than relying on the defer to outrun it.
      //
      // If the write fails or is refused, the echo carries the real value, the
      // changed-check fires, and the menu corrects itself.
      brandState = { ...brandState, projectTheme: brand };
      getProjectWindow()?.webContents.send(IpcChannels.menuSetProjectTheme, brand);
    };
    return [
      {
        label: `App default (${BRANDS[brandState.appBrand].label})`,
        type: 'radio',
        checked: brandState.projectTheme === null,
        click: () => choose(null),
      },
      ...BRAND_IDS.map(
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
