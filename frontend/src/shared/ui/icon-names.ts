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
  /*
    IKONKALI AMAL TUGMALARI uchun to'plam (`IconButton`). Talab: *"har bir
    o'quvchi bo'yicha actions buttonlar icon ko'rinishida bo'lgani ma'qul"* —
    matnli tugmalar o'rniga qatorda ikonka turadi, ya'ni har amal uchun
    ANIQ tanib olinadigan shakl kerak.

    ⚠️ `play`, `mic`, `eye`, `paperclip` bu ro'yxatda YO'Q: ular allaqachon
    yuqorida bor (jonli dars va vazifa ekranlaridan).

    ⚠️ `video` — DARS VIDEOSI (to'rtburchak ichida "play"), `camera` esa
    videoqo'ng'iroq kamerasi. Ikkisi boshqa narsa, almashtirib ishlatilmasin.
  */
  | 'pause'
  | 'arrow-right-left'
  | 'user-x'
  | 'upload'
  | 'image'
  | 'video'
  | 'link-off'
  | 'wallet'
  | 'note'
  | 'chevron-left'
  /*
    `sliders` — tizim sozlamalari menyusi. Eski ilovada bunday bo'lim UMUMAN
    bo'lmagan (muhit o'zgaruvchilari qo'lda tahrirlanardi), shuning uchun
    ko'chiriladigan sprite shakli ham yo'q. Tishli g'ildirak o'rniga
    "regulyatorlar" tanlandi: bo'lim mazmuni aynan qiymatlarni sozlash, va
    bu shakl menyudagi mavjud `grid`/`chart` ikonkalariga o'xshab ketmaydi.
  */
  | 'sliders'
