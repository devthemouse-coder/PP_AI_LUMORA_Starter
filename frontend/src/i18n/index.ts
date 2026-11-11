import { createI18n } from 'vue-i18n'

import en from './locales/en.json'
import ko from './locales/ko.json'

const i18n = createI18n({
  legacy: false,
  locale: 'ko',
  fallbackLocale: 'en',
  messages: {
    en,
    ko,
  },
})

export default i18n
