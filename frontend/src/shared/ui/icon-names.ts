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
  /*
    R35/R36 — BILDIRISHNOMA QO'NG'IROQCHASI.

    ★ NEGA ALOHIDA BLOK OXIRDA: bu faylga bir necha tarmoq AYNI vaqtda
    qo'shmoqda; ro'yxat o'rtasiga qistirilgan qator merge paytida
    to'qnashuv beradi, oxiridagi blok esa bermaydi.

    ⚠️ `alert` BILAN ALMASHTIRILMADI: uchburchak ichidagi undov — XATO
    belgisi (`DataStatus`, forma xatolari shu shakldan foydalanadi).
    Qo'ng'iroqcha esa neytral: "yangilik bor" degani, "nimadir buzildi"
    emas. Ikkalasi bir shakl bo'lsa, har baho o'quvchiga xato bo'lib
    ko'rinardi.
  */
  | 'bell'
