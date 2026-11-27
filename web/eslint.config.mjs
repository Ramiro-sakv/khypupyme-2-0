// Reúne las reglas de ESLint para JavaScript, TypeScript y los hooks de React. Los archivos generados se revisan mediante sus herramientas de origen.

import js from "@eslint/js";
import reactHooks from "eslint-plugin-react-hooks";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist", "src/api/schema.d.ts", "android/**/build/", "android/app/src/main/assets/", "src-tauri/target/"] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  reactHooks.configs.flat.recommended,
);
