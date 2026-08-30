import js from '@eslint/js'
import vueTsConfigs from '@vue/eslint-config-typescript'
import pluginVue from 'eslint-plugin-vue'

export default [
  {
    ignores: ['dist/**', 'node_modules/**', 'public/**'],
  },
  js.configs.recommended,
  ...pluginVue.configs['flat/recommended'],
  ...vueTsConfigs(),
  {
    rules: {
      // SPEC 9.9 — `v-html` qat'iyan taqiqlanadi (XSS).
      'vue/no-v-html': 'error',
      'vue/multi-word-component-names': 'off',
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/consistent-type-imports': ['error', { prefer: 'type-imports' }],
      // Kutubxona callback'larida (LiveKit) argument tartibi qat'iy: kerakmas
      // argumentni O'CHIRIB bo'lmaydi, faqat `_` bilan belgilash mumkin.
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_' },
      ],
      'no-console': ['warn', { allow: ['warn', 'error'] }],
    },
  },
  /*
    BUILD SKRIPTLARI — BRAUZER EMAS, NODE MUHITI (2026-08-30).

    `scripts/` ichidagi fayllar `npm run build` paytida Node'da ishlaydi:
    ular `process.env` ni o'qiydi va build jarayoni haqida konsolga yozadi.

    ★ NEGA UMUMIY QOIDA O'ZGARTIRILMADI: `process` ni hamma joyda ochib
      qo'yish ILOVA kodida ham unga murojaat qilish yo'lini ochardi —
      u yerda esa `process` yo'q va bunday kod brauzerda yiqilardi.

    ★ `console.log` bu yerda RUXSAT: build logi — skriptning yagona
      chiqish kanali. Ilovada esa u hamon ogohlantirish beradi.
  */
  {
    files: ['scripts/**/*.{js,mjs,ts}', 'vite.config.ts'],
    languageOptions: {
      globals: {
        process: 'readonly',
        console: 'readonly',
      },
    },
    rules: {
      'no-console': 'off',
    },
  },
]
