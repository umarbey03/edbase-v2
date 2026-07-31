<script setup lang="ts">
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import {
  canResetSetting,
  isToggleOn,
  replaceSettingInPage,
  resetSetting,
  SETTINGS_QUERY_KEY,
  settingDisplayText,
  settingOriginLabel,
  settingOriginTone,
  toggleValueText,
  updateSetting,
} from '@/entities/setting'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { formatSum, parseMoneyInput } from '@/shared/lib/money'
import type { SettingDto, SettingsPageDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseField, ConfirmDeleteDialog } from '@/shared/ui'

/**
 * BITTA sozlama qatori: tavsif, manba, forma elementi va o'z "Saqlash" tugmasi.
 *
 * ★★ NEGA MUTATSIYA AYNAN SHU YERDA (sahifada emas):
 * `PUT /settings/{key}` — mustaqil resurs. Saqlash holati (`isPending`),
 * xato va tasdiq matni HAM shu qatorga tegishli. Ularni sahifaga ko'tarsak,
 * "qaysi kalit saqlanyapti" ni kalit bo'yicha xaritada yuritish kerak
 * bo'lardi — bitta maydonni saqlaganda 19 ta tugmaning hammasi bloklanib
 * qolish xavfi bilan. Har qator o'z holatini o'zi boshqaradi.
 *
 * ★ SIR BILAN ISHLASH bu komponentning eng nozik joyi — pastdagi
 * `SECRET`, `syncFromServer` va `canSave` izohlariga qarang.
 */
const props = defineProps<{ setting: SettingDto }>()

const queryClient = useQueryClient()

/**
 * Formadagi joriy qiymat — DOIM SATR.
 *
 * Toggle uchun ham satr (`"true"`/`"false"`): serverga aynan shu ketadi va
 * "ekranda ko'rgan qiymat" bilan "yuborilgan qiymat" hech qachon farq
 * qilmaydi. Belgilash oynasi bilan bog'lanish `toggleOn` orqali.
 */
const draft = ref('')

const errorMessage = ref<string | null>(null)
const savedNote = ref<string | null>(null)
const resetOpen = ref(false)
const resetError = ref<string | null>(null)

/** "Saqlandi" yozuvini o'chiradigan taymer — komponent yo'q qilinsa bekor qilinadi. */
let savedNoteTimer: number | null = null

function clearSavedNote(): void {
  if (savedNoteTimer !== null) {
    window.clearTimeout(savedNoteTimer)
    savedNoteTimer = null
  }
  savedNote.value = null
}

/**
 * ★★★ SIRNI FORMAGA HECH QACHON QO'YMAYMIZ.
 *
 * Server sir uchun `value` ni `null` qilib yuboradi va faqat `maskedValue`
 * (`••••••••cret`) keladi. Agar maskani maydonga yozib qo'ysak, admin uni
 * o'zgartirmasdan "Saqlash" bosganda serverga AYNAN `"••••••••cret"`
 * satri ketardi va haqiqiy sir shu bema'ni matn bilan almashib, Telegram
 * yoki fayl ombori jimgina ishlamay qolardi.
 *
 * Shuning uchun sir maydoni DOIM BO'SH boshlanadi va bo'sh holatda
 * saqlash mumkin emas (`canSave`). "Bo'sh yuborib sirni o'chirib yuborish"
 * ssenariysi shu ikki qoida bilan yopilgan.
 */
function syncFromServer(): void {
  draft.value = props.setting.isSecret ? '' : (props.setting.value ?? '')
  errorMessage.value = null
}

/*
  Faqat SERVER QIYMATI o'zgarganda formani qayta to'ldiramiz.

  `props.setting` obyektining o'zini kuzatsak, ro'yxat qayta yuklanganda
  (mazmuni bir xil bo'lsa ham yangi obyekt keladi) admin terayotgan matn
  yo'qolardi. Shu sababli kuzatuv "imzo" bo'yicha: qiymat, manba yoki
  maska haqiqatan almashgandagina forma qayta o'qiladi.
*/
watch(
  () => [
    props.setting.key,
    props.setting.value,
    props.setting.origin,
    props.setting.isSet,
    props.setting.maskedValue,
  ],
  syncFromServer,
  { immediate: true },
)

/* ============================================================= ko'rinish === */

const constraints = computed(() => props.setting.constraints)

const originTone = computed(() => settingOriginTone(props.setting.origin))
const originLabel = computed(() => settingOriginLabel(props.setting.origin))

const showReset = computed(() => canResetSetting(props.setting))

/** Sir uchun joriy holat: maska yoki "Kiritilmagan". */
const currentText = computed(() => settingDisplayText(props.setting))

const updatedText = computed(() =>
  props.setting.updatedAt === null ? null : formatDateTime(props.setting.updatedAt),
)

/**
 * Maydon ostidagi ishora — CHEGARALAR SERVERDAN, kodda takrorlanmaydi.
 *
 * `minimum`/`maximum`/`maxLength`/`format` ning hammasi `constraints` dan
 * o'qiladi: server chegarani o'zgartirsa, ishora ham o'zi o'zgaradi.
 */
const hint = computed(() => {
  /*
    Tahrirlanmaydigan sozlamada ishora KERAK EMAS: "500 belgigacha" degan
    yozuv o'chirilgan maydon ostida shovqindan boshqa narsa emas — u yerda
    o'qilishi kerak bo'lgan matn `readOnlyReason` da.
  */
  if (!props.setting.isEditable) return ''

  const parts: string[] = []
  const { minimum, maximum, maxLength, format } = constraints.value

  if (props.setting.kind === 'Number' || props.setting.kind === 'Money') {
    if (minimum !== null && maximum !== null) parts.push(`${minimum} … ${maximum}`)
    else if (minimum !== null) parts.push(`kamida ${minimum}`)
    else if (maximum !== null) parts.push(`ko‘pi bilan ${maximum}`)
  } else if (props.setting.kind !== 'Toggle' && props.setting.kind !== 'Choice') {
    parts.push(`${maxLength} belgigacha`)
  }

  if (format === 'Url') parts.push('to‘liq manzil (https://…)')
  if (format === 'TimeZone') parts.push('IANA zona nomi (Asia/Tashkent)')

  // Pul kiritilganda "540 000 so'm" ko'rinishi — nolni ortiqcha yozib
  // yuborish eng qimmat xato, uni ko'z bilan darrov ilg'ash kerak.
  if (props.setting.kind === 'Money') {
    const parsed = parseMoneyInput(draft.value)
    if (parsed !== null) parts.push(formatSum(parsed))
  }

  return parts.join(' · ')
})

/** Belgilash oynasi bilan satr o'rtasidagi ko'prik. */
const toggleOn = computed({
  get: (): boolean => isToggleOn(draft.value),
  set: (on: boolean): void => {
    draft.value = toggleValueText(on)
  },
})

/* ============================================================== saqlash === */

/**
 * Serverga ketadigan satr.
 *
 * Son va pulda BO'SHLIQ olib tashlanadi va vergul nuqtaga aylanadi: ishorada
 * summa `540 000 so'm` ko'rinishida (uzilmas bo'shliq bilan) chiziladi va
 * admin uni maydonga nusxalab qo'yishi tabiiy — tozalamasak server "Son
 * kiriting" deb 400 qaytarardi. Mobil klaviaturada nuqta o'rniga vergul
 * chiqishi ham shu yerda hal bo'ladi (`parseMoneyInput` bilan bir xil qoida).
 *
 * Sirda `trim()` ATAYLAB: token nusxalanganda oxirida ko'rinmas bo'shliq
 * yoki qator ko'chirish ilashib keladi va u bilan yozilgan sir hech qachon
 * mos kelmasdi. Boshi/oxiri bo'shliqli sir esa amalda uchramaydi.
 */
const payloadValue = computed(() => {
  const raw = draft.value
  if (props.setting.kind === 'Toggle') return toggleValueText(isToggleOn(raw))
  if (props.setting.kind === 'Number' || props.setting.kind === 'Money') {
    // Uzilmas bo'shliq (U+00A0) `\s` ga kiradi, lekin u ALOHIDA yozilgan:
    // `formatMoney` aynan shu belgini qo'yadi va nusxa-joylash orqali
    // maydonga qaytib tushadi (`parseMoneyInput` dagi bilan bir xil sabab).
    return raw.replace(/[\s\u00A0]/g, '').replace(',', '.')
  }
  return raw.trim()
})

/**
 * O'zgarish BORMI.
 *
 * Sirda "o'zgarish" = maydonda matn bor. Boshqa turlarda server qiymati bilan
 * taqqoslanadi, Toggle esa MA'NO bo'yicha ("True" va "true" bir xil qiymat —
 * satr sifatida farq qiladi, shuning uchun tugma o'z-o'zidan yonib turardi).
 */
const isDirty = computed(() => {
  if (props.setting.isSecret) return draft.value.trim().length > 0
  if (props.setting.kind === 'Toggle') return toggleOn.value !== isToggleOn(props.setting.value)
  return payloadValue.value !== (props.setting.value ?? '')
})

const saveMutation = useMutation({
  mutationFn: (value: string) => updateSetting(props.setting.key, { value }),
  onSuccess: (updated: SettingDto) => {
    applyUpdated(updated)
    clearSavedNote()
    savedNote.value = 'Saqlandi.'
    // Tasdiq abadiy osilib qolmasin: keyingi tahrirda u eski amalga tegishli
    // bo'lib, "saqlandi" degan yolg'on taassurot qoldirardi.
    savedNoteTimer = window.setTimeout(() => {
      savedNote.value = null
      savedNoteTimer = null
    }, 4000)
  },
  onError: (error: Error) => {
    /*
      XATO MATNI SERVERNIKI.

      `toUserMessage` 400 da `problem.errors[key][0]` ni o'qiydi — u yerda
      yo validatsiya sababi ("Qiymat 100000000 dan katta bo'lmasin"), yo
      "faqat o'qish" izohi turadi. O'zimiz matn yig'sak, aynan shu foydali
      jumlalar yo'qolardi.
    */
    errorMessage.value = toUserMessage(error)
  },
})

const resetMutation = useMutation({
  mutationFn: () => resetSetting(props.setting.key),
  onSuccess: (updated: SettingDto) => {
    applyUpdated(updated)
    resetOpen.value = false
    resetError.value = null
    clearSavedNote()
    savedNote.value = 'Standart qiymatga qaytarildi.'
    savedNoteTimer = window.setTimeout(() => {
      savedNote.value = null
      savedNoteTimer = null
    }, 4000)
  },
  onError: (error: Error) => {
    // Oyna OCHIQ qoladi (`ConfirmDeleteDialog` naqshi): sabab aynan shu
    // yerda o'qiladi, oyna yopilsa uni ko'rsatadigan joy qolmasdi.
    resetError.value = toUserMessage(error)
  },
})

/**
 * Javobdagi `SettingDto` bilan keshni nuqtali yangilaydi.
 *
 * Butun ro'yxatni `invalidate` qilmaymiz — qo'shni qatorlarda tahrir
 * davom etayotgan bo'lishi mumkin va ular server qiymatiga qaytib ketardi.
 */
function applyUpdated(updated: SettingDto): void {
  errorMessage.value = null
  queryClient.setQueryData<SettingsPageDto>(SETTINGS_QUERY_KEY, (page) =>
    page === undefined ? page : replaceSettingInPage(page, updated),
  )
}

/**
 * Saqlash MUMKINMI.
 *
 * ★ `isDirty` sirda "maydonda matn bor" degani — ya'ni BO'SH sir maydoni
 * bilan tugma umuman faollashmaydi. Bu "maskani ko'rib, tegmasdan Saqlash"
 * ssenariysining ikkinchi to'sig'i (birinchisi — maskani formaga umuman
 * qo'ymaslik).
 *
 * `isPending` ni ham hisobga oladi: tugma bosilgach o'chadi va ikkinchi
 * `PUT` yuborilmaydi.
 */
const canSave = computed(
  () => props.setting.isEditable && isDirty.value && !saveMutation.isPending.value,
)

function save(): void {
  if (!canSave.value) return
  errorMessage.value = null
  clearSavedNote()
  saveMutation.mutate(payloadValue.value)
}

function openReset(): void {
  resetError.value = null
  resetOpen.value = true
}

onBeforeUnmount(() => {
  clearSavedNote()
  /*
    Qatorni tark etganda terilgan sirni xotirada qoldirmaymiz. Komponent
    baribir yo'q qilinadi, lekin bu satr NIYATNI hujjatlashtiradi: sir
    hech qayerda (kesh, `localStorage`, store) saqlanmaydi — u faqat
    yuborilgunicha shu `ref` da yashaydi.
  */
  draft.value = ''
})
</script>

<template>
  <div class="grid gap-3 py-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,20rem)] lg:gap-6">
    <!-- ============================ tavsif ============================= -->
    <div class="min-w-0">
      <div class="flex flex-wrap items-center gap-2">
        <p
          class="text-sm font-semibold text-slate-100"
          v-text="props.setting.name"
        />
        <BaseBadge :tone="originTone">
          {{ originLabel }}
        </BaseBadge>
        <BaseBadge
          v-if="!props.setting.isEditable"
          tone="neutral"
        >
          Faqat o‘qish
        </BaseBadge>
        <BaseBadge
          v-if="props.setting.isSecret"
          tone="danger"
        >
          Sir
        </BaseBadge>
      </div>

      <p
        class="mt-1 font-mono text-[11px] text-dim"
        v-text="props.setting.key"
      />

      <p
        class="mt-1.5 text-xs leading-relaxed text-slate-400"
        v-text="props.setting.description"
      />

      <!--
        ★ "Faqat o'qish" SABABI — YASHIRILMAYDI.
        Bu matn foydalanuvchi uchun yozilgan va eng muhim savolga javob
        beradi: "nega o'zgartira olmayapman va qayerdan o'zgartiraman".
        Uni olib tashlasak, o'chirilgan maydon buzuq ko'rinardi.
      -->
      <p
        v-if="!props.setting.isEditable && props.setting.readOnlyReason !== null"
        class="mt-2 flex gap-2 rounded-lg border border-line bg-ink-800 p-2.5 text-[11px] leading-relaxed text-slate-300"
      >
        <AppIcon
          name="lock"
          :size="13"
          class="mt-0.5 text-slate-500"
        />
        <span v-text="props.setting.readOnlyReason" />
      </p>

      <p
        v-if="updatedText !== null"
        class="mt-2 text-[11px] text-dim"
      >
        Oxirgi o‘zgarish: {{ updatedText }}
      </p>
    </div>

    <!-- ============================ boshqaruv ========================== -->
    <div class="min-w-0">
      <!--
        ★★ SIR: joriy holat FAQAT maska sifatida. Server sirning o'zini
        qaytarmaydi, shuning uchun bu yerda ko'rsatadigan narsa ham yo'q —
        "ko'rsatish" (ko'z) tugmasi ATAYLAB QO'YILMAGAN.
      -->
      <div
        v-if="props.setting.isSecret"
        class="mb-2 flex items-center gap-2 rounded-lg border border-line bg-ink-800 px-3 py-2"
      >
        <AppIcon
          name="lock"
          :size="14"
          class="text-slate-500"
        />
        <span
          class="min-w-0 flex-1 truncate font-mono text-xs text-slate-300"
          v-text="currentText"
        />
      </div>

      <!-- Sir + tahrirlanadi: YANGI qiymat uchun BO'SH maydon. -->
      <BaseField
        v-if="props.setting.isSecret && props.setting.isEditable"
        label="Yangi sir"
        hint="Bo‘sh qoldirilsa mavjud sir o‘zgarmaydi."
      >
        <!--
          BRAUZER PAROL MENEJERI BU MAYDONGA ARALASHMASLIGI UCHUN uch chora:

           1) `autocomplete="off"` — avtoto'ldirish so'ralmaydi;
           2) `name` ATAYLAB QO'YILMAGAN — menejerlar maydonni nomi bo'yicha
              ham taniydi va "parol" deb hisoblaydi;
           3) maydon `<form>` ICHIDA EMAS va saqlash `fetch` orqali ketadi —
              Chrome'ning "parolni saqlaymizmi?" taklifi forma yuborilishiga
              bog'liq, ya'ni bu yerda umuman chiqmaydi.

          `type="password"` esa terilayotgan sirni yelka orqasidan o'qishdan
          saqlaydi. Uni "ko'rsatish" tugmasi YO'Q — bu sahifada sir hech
          qachon ochiq matn bo'lib ekranga chiqmaydi.
        -->
        <input
          v-model="draft"
          class="zn-input font-mono"
          type="password"
          autocomplete="off"
          spellcheck="false"
          :maxlength="constraints.maxLength"
          :placeholder="props.setting.isSet ? 'Yangi qiymat kiriting' : 'Kiritilmagan'"
        >
      </BaseField>

      <!-- =================== SIR EMAS: turga qarab maydon =================== -->
      <BaseField
        v-else-if="!props.setting.isSecret"
        :label="props.setting.kind === 'Toggle' ? 'Holat' : 'Qiymat'"
        :hint="hint"
      >
        <div
          v-if="props.setting.kind === 'Toggle'"
          class="flex h-11 items-center gap-2.5"
        >
          <input
            v-model="toggleOn"
            class="size-5 shrink-0 accent-brand-500"
            type="checkbox"
            :disabled="!props.setting.isEditable"
          >
          <span
            class="text-sm"
            :class="toggleOn ? 'font-semibold text-brand-400' : 'text-slate-400'"
            v-text="toggleOn ? 'Yoqilgan' : 'O‘chiq'"
          />
        </div>

        <select
          v-else-if="props.setting.kind === 'Choice'"
          v-model="draft"
          class="zn-input"
          :disabled="!props.setting.isEditable"
        >
          <!--
            Variantlar SERVERDAN (`constraints.choices`). Ro'yxatni kodda
            takrorlasak, backend yangi variant qo'shganda u paneldan
            tanlanmay qolardi.
          -->
          <option
            v-for="choice in constraints.choices"
            :key="choice"
            :value="choice"
          >
            {{ choice }}
          </option>
        </select>

        <input
          v-else
          v-model="draft"
          class="zn-input"
          :class="
            props.setting.kind === 'Number' || props.setting.kind === 'Money'
              ? 'tabular-nums'
              : ''
          "
          type="text"
          :inputmode="
            props.setting.kind === 'Number' || props.setting.kind === 'Money'
              ? 'numeric'
              : constraints.format === 'Url'
                ? 'url'
                : 'text'
          "
          autocomplete="off"
          spellcheck="false"
          :maxlength="constraints.maxLength"
          :disabled="!props.setting.isEditable"
          placeholder="Kiritilmagan"
        >
      </BaseField>

      <!-- ============================ xabarlar ========================== -->
      <p
        v-if="errorMessage !== null"
        class="mt-2 rounded-lg border border-rose-500/25 bg-rose-500/10 p-2.5 text-[11px] leading-relaxed text-rose-200"
        role="alert"
        v-text="errorMessage"
      />
      <p
        v-else-if="savedNote !== null"
        class="mt-2 text-[11px] font-medium text-green-400"
        role="status"
        v-text="savedNote"
      />

      <!-- ============================= tugmalar ========================= -->
      <div
        v-if="props.setting.isEditable || showReset"
        class="mt-2.5 flex flex-wrap items-center justify-end gap-2"
      >
        <!--
          ★ "Standartga qaytarish" FAQAT `origin === 'Database'` da.
          Boshqa manbalarda server 400 beradi — bosilishi mumkin bo'lgan,
          lekin doim xato beradigan tugma foydalanuvchini chalg'itardi.
        -->
        <BaseButton
          v-if="showReset"
          size="sm"
          variant="ghost"
          :disabled="resetMutation.isPending.value"
          @click="openReset"
        >
          <template #icon>
            <AppIcon
              name="refresh"
              :size="13"
            />
          </template>
          Standartga qaytarish
        </BaseButton>

        <BaseButton
          v-if="props.setting.isEditable"
          size="sm"
          :disabled="!canSave"
          :loading="saveMutation.isPending.value"
          @click="save"
        >
          Saqlash
        </BaseButton>
      </div>
    </div>

    <!--
      Tasdiq oynasi — mavjud `ConfirmDeleteDialog` naqshi: xato kelganda
      oyna YOPILMAYDI va server sababi aynan shu yerda o'qiladi.
    -->
    <ConfirmDeleteDialog
      :open="resetOpen"
      title="Standart qiymatga qaytarish"
      :message="`“${props.setting.name}” uchun paneldan yozilgan qiymat o‘chiriladi va sozlama serverdagi (muhit yoki standart) qiymatga qaytadi. O‘zgarish darhol kuchga kiradi.`"
      :pending="resetMutation.isPending.value"
      :error="resetError"
      confirm-label="Qaytarish"
      @close="resetOpen = false"
      @confirm="resetMutation.mutate()"
    />
  </div>
</template>
