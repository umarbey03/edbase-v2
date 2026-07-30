/**
 * Ikonka nomlari ALOHIDA faylda: `<script setup>` blokidan tur eksport qilib
 * bo'lmaydi, lekin boshqa komponentlar (`EmptyState`, navigatsiya) ikonka
 * nomini prop sifatida qabul qiladi va uni QAT'IY tekshirish kerak.
 */
export type IconName =
  | 'mic'
  | 'mic-off'
  | 'camera'
  | 'camera-off'
  | 'screen-share'
  | 'hand'
  | 'chat'
  | 'leave'
  | 'users'
  | 'send'
  | 'arrow-down'
  | 'arrow-up'
  | 'arrow-left'
  | 'trash'
  | 'close'
  | 'logout'
  | 'calendar'
  | 'refresh'
  | 'wifi-off'
  | 'chevron-down'
  | 'lock'
  | 'mail'
  | 'play'
  | 'check'
  | 'menu'
  | 'search'
  | 'plus'
  | 'edit'
  | 'clipboard'
  | 'file-text'
  | 'grid'
  | 'chevron-right'
  | 'alert'
  | 'clock'
  | 'star'
  | 'award'
  | 'paperclip'
  | 'download'
  | 'list'
  | 'eye'
  | 'eye-off'
  /*
    Quyidagi oltitasi eski o'quvchi ilovasining (`student.html`) ikonka
    sprite'idan AYNAN ko'chirilgan: pastki 5 tab (`i-home`, `i-cal`, `i-book`,
    `i-chart`, `i-chat`) va dars turi nishonlari (`i-cap`, `i-assist`).
    Ikonka shakli o'zgarsa o'quvchi tabni ko'z bilan topa olmay qoladi.
  */
  | 'home'
  | 'book'
  | 'chart'
  | 'graduation'
  | 'user-check'
  | 'message-circle'
  /*
    Eski USTOZ panelining (`teacher.html`) sprite'idan AYNAN ko'chirilgan
    to'rtta shakl. Ustoz guruh ichidagi tabni ikonkasi bo'yicha topadi
    (`i-att` davomat, `i-board` reyting, `i-student` o'quvchilar), shuning
    uchun mavjud "o'xshash" ikonkalar bilan almashtirilmadi.
    `phone` — kuratorlik jadvalidagi qo'ng'iroq tugmasi (`i-phone`).
  */
  | 'check-square'
  | 'trophy'
  | 'user'
  | 'phone'
