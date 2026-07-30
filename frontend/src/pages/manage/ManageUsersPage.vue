<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  activateUser,
  deactivateUser,
  fetchUsers,
  ROLE_OPTIONS,
  roleLabel,
  roleTone,
  USER_SEARCH_MIN,
} from '@/entities/user'
import UserFormDialog from '@/features/user-form/ui/UserFormDialog.vue'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { formatDateTime } from '@/shared/lib/datetime'
import type { UserDetailsDto, UserRoleName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  PageHeader,
  PaginationBar,
} from '@/shared/ui'

/**
 * Foydalanuvchilar CRM'i (Academic/Admin).
 *
 * Bazada 1500+ foydalanuvchi bor — shuning uchun qidiruv SERVERDA, sahifalash
 * ham serverda. Qidiruv kechiktiriladi (har harfda so'rov yuborilmasin).
 */
const queryClient = useQueryClient()

const search = ref('')
const debouncedSearch = useDebounced(search)
const roleFilter = ref<UserRoleName | ''>('')
const activeFilter = ref<'' | 'true' | 'false'>('')
const page = ref(1)

const PAGE_SIZE = 20

/*
  ★ QIDIRUVNING MINIMAL UZUNLIGI serverda tekshiriladi
  (`UserService.MinSearchLength = 3`) va qisqa satr 400 bilan qaytadi.
  Ilgari bir harf yozilishi bilanoq so'rov ketardi: `DataStatus` xato holatiga
  o'tib JADVAL BUTUNLAY YO'QOLARDI va o'rniga qizil banner chiqardi — 3-harfda
  o'ziga kelsa ham, oraliqda interfeys buzilgan ko'rinardi. Endi qisqa satr
  UMUMAN yuborilmaydi, o'rniga maydon ostida nima kutilayotgani aytiladi.
*/
const searchTerm = computed(() => debouncedSearch.value.trim())
const searchTooShort = computed(
  () => searchTerm.value.length > 0 && searchTerm.value.length < USER_SEARCH_MIN,
)
const effectiveSearch = computed(() =>
  searchTerm.value.length >= USER_SEARCH_MIN ? searchTerm.value : undefined,
)

// Filtr o'zgarsa 1-sahifaga qaytamiz, aks holda "10-sahifada natija yo'q" holati chiqadi.
watch([effectiveSearch, roleFilter, activeFilter], () => {
  page.value = 1
})

const usersQuery = useQuery({
  queryKey: ['users', effectiveSearch, roleFilter, activeFilter, page],
  queryFn: ({ signal }) =>
    fetchUsers(
      {
        search: effectiveSearch.value,
        role: roleFilter.value === '' ? undefined : roleFilter.value,
        isActive: activeFilter.value === '' ? undefined : activeFilter.value === 'true',
        page: page.value,
        pageSize: PAGE_SIZE,
      },
      { signal },
    ),
})

const users = computed(() => usersQuery.data.value?.items ?? [])
const total = computed(() => usersQuery.data.value?.total ?? 0)
const totalPages = computed(() => usersQuery.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  usersQuery.error.value !== null ? toUserMessage(usersQuery.error.value) : null,
)

/* --------------------------- yaratish/tahrirlash --------------------------- */

const dialogOpen = ref(false)
const editing = ref<UserDetailsDto | null>(null)

function openCreate(): void {
  editing.value = null
  dialogOpen.value = true
}

function openEdit(user: UserDetailsDto): void {
  editing.value = user
  dialogOpen.value = true
}

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['users'] })
}

const toggleMutation = useMutation({
  mutationFn: (user: UserDetailsDto) =>
    user.isActive ? deactivateUser(user.id) : activateUser(user.id),
  onSuccess: refresh,
})
</script>

<template>
  <div>
    <PageHeader
      title="Foydalanuvchilar"
      :subtitle="`Jami: ${total} ta hisob`"
    >
      <template #actions>
        <BaseButton @click="openCreate">
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          Yangi
        </BaseButton>
      </template>
    </PageHeader>

    <!-- Filtrlar: telefonda ustun, sm dan boshlab yonma-yon -->
    <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
      <div class="relative sm:col-span-2 lg:col-span-2">
        <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
          <AppIcon
            name="search"
            :size="16"
          />
        </span>
        <input
          v-model="search"
          class="zn-input pl-9"
          placeholder="Ism yoki email bo‘yicha qidirish"
        >
        <p
          v-if="searchTooShort"
          class="mt-1 text-[11px] text-dim"
        >
          Qidirish uchun kamida {{ USER_SEARCH_MIN }} belgi kiriting.
        </p>
      </div>
      <select
        v-model="roleFilter"
        class="zn-input"
        aria-label="Rol bo‘yicha filtr"
      >
        <option value="">
          Barcha rollar
        </option>
        <option
          v-for="option in ROLE_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>
      <select
        v-model="activeFilter"
        class="zn-input"
        aria-label="Holat bo‘yicha filtr"
      >
        <option value="">
          Barcha holatlar
        </option>
        <option value="true">
          Faol
        </option>
        <option value="false">
          Bloklangan
        </option>
      </select>
    </div>

    <DataStatus
      :pending="usersQuery.isPending.value"
      :error="errorMessage"
      :empty="users.length === 0"
      :retrying="usersQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="users"
      empty-title="Foydalanuvchi topilmadi"
      empty-text="Qidiruv shartlarini o‘zgartirib ko‘ring."
      @retry="usersQuery.refetch()"
    >
      <BaseCard flush>
        <!-- Telefon: kartochka ro'yxati -->
        <ul class="divide-y divide-line md:hidden">
          <li
            v-for="user in users"
            :key="user.id"
            class="p-3.5"
          >
            <div class="flex items-start justify-between gap-2">
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="user.fullName ?? '—'"
              />
              <BaseBadge :tone="roleTone(user.role ?? '')">
                {{ roleLabel(user.role ?? '—') }}
              </BaseBadge>
            </div>
            <p
              class="mt-1 truncate text-xs text-slate-400"
              v-text="user.email ?? '—'"
            />
            <p
              v-if="user.phone !== null"
              class="text-xs text-dim"
              v-text="user.phone"
            />
            <div class="mt-2.5 flex flex-wrap items-center gap-2">
              <BaseBadge :tone="user.isActive ? 'success' : 'danger'">
                {{ user.isActive ? 'Faol' : 'Bloklangan' }}
              </BaseBadge>
              <span class="flex-1" />
              <BaseButton
                size="sm"
                variant="secondary"
                @click="openEdit(user)"
              >
                <template #icon>
                  <AppIcon
                    name="edit"
                    :size="13"
                  />
                </template>
                Tahrirlash
              </BaseButton>
              <BaseButton
                size="sm"
                :variant="user.isActive ? 'danger' : 'success'"
                :loading="toggleMutation.isPending.value"
                @click="toggleMutation.mutate(user)"
              >
                {{ user.isActive ? 'Bloklash' : 'Faollashtirish' }}
              </BaseButton>
            </div>
          </li>
        </ul>

        <!-- Desktop: jadval. Gorizontal skroll SHU konteynerda. -->
        <div class="scroll-x-safe scrollbar-slim hidden md:block">
          <table class="zn-table">
            <thead>
              <tr>
                <th>Ism</th>
                <th>Email</th>
                <th>Telefon</th>
                <th>Rol</th>
                <th>Holat</th>
                <th>Qo‘shilgan</th>
                <th />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="user in users"
                :key="user.id"
              >
                <td
                  class="font-medium text-slate-100"
                  v-text="user.fullName ?? '—'"
                />
                <td
                  class="text-slate-400"
                  v-text="user.email ?? '—'"
                />
                <td
                  class="text-slate-400"
                  v-text="user.phone ?? '—'"
                />
                <td>
                  <BaseBadge :tone="roleTone(user.role ?? '')">
                    {{ roleLabel(user.role ?? '—') }}
                  </BaseBadge>
                </td>
                <td>
                  <BaseBadge :tone="user.isActive ? 'success' : 'danger'">
                    {{ user.isActive ? 'Faol' : 'Bloklangan' }}
                  </BaseBadge>
                </td>
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDateTime(user.createdAt)"
                />
                <td>
                  <div class="flex items-center justify-end gap-2">
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="openEdit(user)"
                    >
                      <template #icon>
                        <AppIcon
                          name="edit"
                          :size="13"
                        />
                      </template>
                      Tahrirlash
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      :variant="user.isActive ? 'danger' : 'success'"
                      :loading="toggleMutation.isPending.value"
                      @click="toggleMutation.mutate(user)"
                    >
                      {{ user.isActive ? 'Bloklash' : 'Faollashtirish' }}
                    </BaseButton>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <PaginationBar
          :page="page"
          :total-pages="totalPages"
          :total="total"
          @update:page="page = $event"
        />
      </BaseCard>
    </DataStatus>

    <UserFormDialog
      :open="dialogOpen"
      :user="editing"
      @close="dialogOpen = false"
      @saved="refresh"
    />
  </div>
</template>
