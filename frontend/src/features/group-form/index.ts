/**
 * `group-form` feature'ining OMMAVIY yuzasi.
 *
 * ★ Eski `GroupFormDialog.vue` (bitta modal, bitta "Saqlash") O'CHIRILDI,
 * o'rniga `GroupEditDrawer` — bo'limlar bo'yicha saqlash. Ikki nusxa
 * SAQLANMADI: `PUT` to'liq almashtirish semantikasida ikkita payload
 * quruvchi bo'lishi eng xavfli holat (biriga yangi maydon qo'shilib,
 * ikkinchisida unutilsa maydon jimgina `null` ga tushardi).
 */
export { default as GroupEditDrawer } from './ui/GroupEditDrawer.vue'
export { GROUP_SECTION_TITLES } from './model/group-sections'
export type { GroupSectionKey } from './model/group-sections'
