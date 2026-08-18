<script setup lang="ts">
/**
 * "HOZIR JONLI" nishoni — pulsatsiyalanuvchi nuqta bilan (2026-08-18).
 *
 * Loyiha egasi: *"telegram live chatga o'xshab chat qismida live chat
 * bo'lyotganini ko'rsatib turuvchi animation bo'lishi kerak"*.
 *
 * ★ YANGI ANIMATSIYA IXTIRO QILINMADI: `animate-ping-live` loyihada
 * ALLAQACHON bor (`style.css`) va u aynan shu maqsad uchun yozilgan —
 * `NextLessonCard` da o'quvchi ekranida jonli darsni shu bilan
 * ko'rsatadi. Ikkinchi, biroz boshqacha pulsatsiya qo'shilsa ilovada
 * ikki xil "jonli" tili paydo bo'lardi.
 *
 * ★ HARAKATNI KAMAYTIRISH avtomatik: `style.css` dagi global
 * `prefers-reduced-motion` qoidasi barcha animatsiyalarni to'xtatadi —
 * nuqta qoladi, faqat pulsatsiya bo'lmaydi. Ya'ni ma'no YO'QOLMAYDI
 * (rang + matn baribir turadi), bu WCAG 2.3.3 talabi.
 *
 * ★ RANG — `rose`: ilovadagi `live` ohangi (`BaseBadge` `live` toni,
 * `SessionBoard` "Hozir efirda", `RecordingIndicator`) shu rangda.
 */
withDefaults(
  defineProps<{
    /** Matnsiz — faqat nuqta (tor joylar uchun, masalan jadval katagi). */
    dotOnly?: boolean
    label?: string
  }>(),
  { dotOnly: false, label: 'Jonli' },
)
</script>

<template>
  <span
    v-if="dotOnly"
    class="inline-block size-2 shrink-0 animate-ping-live rounded-full bg-rose-500"
    :title="label"
    role="img"
    :aria-label="label"
  />

  <span
    v-else
    class="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-rose-500/12 px-2 py-0.5 text-[11px] font-semibold leading-tight text-rose-200"
  >
    <!--
      Nuqta `aria-hidden`: ma'no yonidagi MATNDA (`BaseBadge` dagi
      `dot` bilan AYNI qaror) — skrinriderga ikki marta o'qilmasin.
    -->
    <span
      class="size-1.5 shrink-0 animate-ping-live rounded-full bg-rose-500"
      aria-hidden="true"
    />
    {{ label }}
  </span>
</template>
