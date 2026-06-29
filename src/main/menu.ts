// The shotAI application menu. File → Settings signals the renderer to open the
// Settings view; Help → About shows app/runtime info. Standard Edit/View/Window
// submenus are kept (via roles) so copy/paste, devtools, and zoom still work.
import {
  app,
  Menu,
  dialog,
  BrowserWindow,
  type MenuItemConstructorOptions,
} from 'electron';
import { IpcChannels } from '../shared/ipc';

/** Build + install the application menu. `getProjectWindow` returns the main
 *  project window — the target for File → Settings and the About dialog parent. */
export function installAppMenu(getProjectWindow: () => BrowserWindow | null): void {
  const isMac = process.platform === 'darwin';

  const showAbout = (): void => {
    const win = getProjectWindow();
    const options: Electron.MessageBoxOptions = {
      type: 'info',
      title: 'About shotAI',
      message: `${app.getName()} ${app.getVersion()}`,
      detail:
        'Local-first SOP builder — record a process and let Claude write the guide.\n\n' +
        `Electron ${process.versions.electron} · Chromium ${process.versions.chrome}\n` +
        `${process.platform}/${process.arch}`,
      buttons: ['OK'],
    };
    if (win) void dialog.showMessageBox(win, options);
    else void dialog.showMessageBox(options);
  };

  const template: MenuItemConstructorOptions[] = [
    ...(isMac ? [{ role: 'appMenu' as const }] : []),
    {
      label: 'File',
      submenu: [
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
    { role: 'viewMenu' },
    { role: 'windowMenu' },
    {
      role: 'help',
      submenu: [{ label: 'About shotAI', click: showAbout }],
    },
  ];

  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}
