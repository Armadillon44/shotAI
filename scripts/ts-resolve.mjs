// Node ESM resolve hook: retry a failed relative import with a .ts extension.
//
// src/main modules import each other extensionlessly ("./msal"), which is correct
// for the Vite/TypeScript build but unresolvable by Node's ESM resolver. Rather than
// littering the app source with .ts specifiers to suit a dev script, this hook lets
// scripts/ (the wif-probe) import the real shipping modules unchanged.
//
// Used with --experimental-transform-types, which compiles the TypeScript itself.
export async function resolve(specifier, context, nextResolve) {
  try {
    return await nextResolve(specifier, context);
  } catch (err) {
    // Only relative specifiers, and only when the miss is a genuine not-found —
    // never mask a real resolution error or rewrite a bare package name.
    if (err?.code === 'ERR_MODULE_NOT_FOUND' && /^\.{1,2}\//.test(specifier)) {
      return await nextResolve(`${specifier}.ts`, context);
    }
    throw err;
  }
}
