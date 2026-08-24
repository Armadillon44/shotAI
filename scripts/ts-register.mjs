// Registers the .ts resolve hook. Use as:
//   node --experimental-transform-types --import ./scripts/ts-register.mjs <script>
import { register } from 'node:module';
register('./ts-resolve.mjs', import.meta.url);
