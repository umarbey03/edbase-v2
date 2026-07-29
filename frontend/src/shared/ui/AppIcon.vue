<script setup lang="ts">
/**
 * Yagona ikonka komponenti — tashqi ikonka kutubxonasi qo'shilmaydi (bundle kichik qoladi).
 * Barcha yo'llar 24×24 setkada, `stroke` uslubida.
 */
type IconName =
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
  | 'arrow-left'
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

const props = withDefaults(
  defineProps<{
    name: IconName
    size?: number
  }>(),
  { size: 20 },
)

const PATHS: Record<IconName, string> = {
  mic: 'M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z M19 10v2a7 7 0 0 1-14 0v-2 M12 19v4 M8 23h8',
  'mic-off':
    'M1 1l22 22 M9 9v3a3 3 0 0 0 5.12 2.12 M15 9.34V4a3 3 0 0 0-5.94-.6 M17 16.95A7 7 0 0 1 5 12v-2 M19 10v2a7 7 0 0 1-.11 1.23 M12 19v4 M8 23h8',
  camera: 'M23 7l-7 5 7 5V7z M14 5H3a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2z',
  'camera-off':
    'M16 16v1a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h2 M10.66 5H14a2 2 0 0 1 2 2v3.34l1 1L23 7v10 M1 1l22 22',
  'screen-share': 'M2 3h20v13H2z M8 21h8 M12 16v5',
  hand: 'M18 11V6a2 2 0 0 0-4 0v5 M14 10V4a2 2 0 0 0-4 0v6 M10 10.5V6a2 2 0 0 0-4 0v8 M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.9-6-2.4l-3.6-3.6a2 2 0 0 1 2.9-2.8L7 15',
  chat: 'M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z',
  leave:
    'M10.7 13.3a16 16 0 0 0 3.4 2.6l1.3-1.3a2 2 0 0 1 2.1-.4 12.8 12.8 0 0 0 2.8.7 2 2 0 0 1 1.7 2v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.4 19.4 0 0 1-3.3-2.7 M5.4 10a19.8 19.8 0 0 1-3.1-8.6A2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7 12.8 12.8 0 0 0 .7 2.8 2 2 0 0 1-.5 2.1L8.1 9.9 M23 1L1 23',
  users:
    'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8z M23 21v-2a4 4 0 0 0-3-3.9 M16 3.1a4 4 0 0 1 0 7.8',
  send: 'M22 2L11 13 M22 2l-7 20-4-9-9-4 20-7z',
  'arrow-down': 'M12 5v14 M19 12l-7 7-7-7',
  'arrow-left': 'M19 12H5 M12 19l-7-7 7-7',
  close: 'M18 6L6 18 M6 6l12 12',
  logout: 'M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4 M16 17l5-5-5-5 M21 12H9',
  calendar: 'M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z',
  refresh: 'M23 4v6h-6 M1 20v-6h6 M3.5 9a9 9 0 0 1 14.9-3.4L23 10 M1 14l4.6 4.4A9 9 0 0 0 20.5 15',
  'wifi-off':
    'M1 1l22 22 M16.7 11.1A11 11 0 0 1 19 12.6 M5 12.6a11 11 0 0 1 5.2-2.4 M10.7 5.1A16 16 0 0 1 22.6 9 M1.4 9a16 16 0 0 1 4.7-2.9 M8.5 16.1a6 6 0 0 1 7 0 M12 20h.01',
  'chevron-down': 'M6 9l6 6 6-6',
  lock: 'M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M7 11V7a5 5 0 0 1 10 0v4',
  mail: 'M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M22 6l-10 7L2 6',
  play: 'M5 3l14 9-14 9V3z',
  check: 'M20 6L9 17l-5-5',
}
</script>

<template>
  <svg
    :width="props.size"
    :height="props.size"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.75"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    focusable="false"
    class="shrink-0"
  >
    <path :d="PATHS[props.name]" />
  </svg>
</template>
