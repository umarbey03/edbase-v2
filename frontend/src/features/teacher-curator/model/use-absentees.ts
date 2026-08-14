import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { fetchSessionAttendance } from '@/entities/attendance'
import type { AttendanceStatusName } from '@/entities/attendance'
import { fetchGroupMembers, fetchGroupSchedule } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import type { GroupDto, ScheduledSessionDto } from '@/shared/types'

/**
 * "Oxirgi darsni qoldirgan o'quvchilar" — eski `#curator-hub` ning yuragi
 * (`/api/teacher/curator/absences`).
 *
 * ★ v2 da BUNDAY YIG'MA ENDPOINT YO'Q, lekin ma'lumot BOR: har guruhning
 * OXIRGI YAKUNLANGAN darsi topiladi va uning davomat varag'i o'qiladi
 * (`GET /live-sessions/{id}/attendance`). Ro'yxat shu yerda yig'iladi.
 *
 * So'rovlar soni ATAYLAB chegaralangan: faqat FAOL va oddiy (`Group`)
 * guruhlar olinadi — kurator guruhining o'zida "qoldirgan" tushunchasi
 * yo'q, arxivlangan guruh esa nazoratdan chiqqan. Har guruh uchun eng ko'pi
 * bilan uchta so'rov (jadval, a'zolar, varaq).
 *
 * ★ `status: null` HAM QOLDIRGAN sanaladi: yozuv yo'q, ya'ni o'quvchi
 * xonaga umuman kirmagan va hech kim uni belgilamagan (server ham buni
 * hisobotda "kelmagan" deb sanaydi).
 */
export interface AbsenteeRow {
  key: string
  studentId: number
  studentName: string
  groupId: number
  groupName: string
  sessionId: number
  sessionStart: string
  status: AttendanceStatusName | null
  /** O'quvchi yoki xodim yozgan sabab. `null` — "Sababsiz". */
  reason: string | null
  /**
   * 🔴 `null` ning IKKI sababi bor (talab R27): raqam kiritilmagan YOKI
   * so'rovchi USTOZ va server kontaktni kesgan. Manba — `GroupMemberDto`,
   * ya'ni kesish aynan shu yerga yetib keladi.
   *
   * Ikkalasida ham natija bir xil: qo'ng'iroq tugmasi CHIZILMAYDI. Kurator
   * uchun raqam BERILADI, ya'ni uning ish oqimi o'zgarmaydi — bu ekran
   * aynan kuratorga mo'ljallangan.
   */
  phone: string | null
}

export interface AbsenteeResult {
  rows: AbsenteeRow[]
  /** Ma'lumoti o'qilmagan guruhlar (403/404) — jimgina yashirmaymiz. */
  failedGroups: string[]
}

function lastEnded(sessions: readonly ScheduledSessionDto[]): ScheduledSessionDto | null {
  const ended = sessions
    .filter((item) => item.status === 'Ended')
    .sort((a, b) => new Date(a.scheduledStart).getTime() - new Date(b.scheduledStart).getTime())
  return ended[ended.length - 1] ?? null
}

async function collectForGroup(
  group: GroupDto,
  signal: AbortSignal | undefined,
): Promise<AbsenteeRow[]> {
  const [schedule, members] = await Promise.all([
    fetchGroupSchedule(group.id, { signal }),
    fetchGroupMembers(group.id, { signal }),
  ])

  const session = lastEnded(schedule)
  if (session === null) return []

  const sheet = await fetchSessionAttendance(session.id, { signal })
  const phones = new Map(members.map((member) => [member.studentId, member.phone]))
  const groupName = group.name ?? `Guruh #${group.id}`

  return (sheet.rows ?? [])
    .filter((row) => row.status === 'Absent' || row.status === null)
    .map((row) => ({
      key: `${session.id}:${row.studentId}`,
      studentId: row.studentId,
      studentName: row.studentName ?? `#${row.studentId}`,
      groupId: group.id,
      groupName,
      sessionId: session.id,
      sessionStart: session.scheduledStart,
      status: row.status,
      reason: row.reason,
      phone: phones.get(row.studentId) ?? null,
    }))
}

export interface AbsenteeQuery {
  rows: ComputedRef<AbsenteeRow[]>
  failedGroups: ComputedRef<string[]>
  pending: ComputedRef<boolean>
  fetching: ComputedRef<boolean>
  errorMessage: ComputedRef<string | null>
  refetch: () => void
}

export function useAbsentees(groups: Ref<GroupDto[]> | ComputedRef<GroupDto[]>): AbsenteeQuery {
  const scanned = computed(() =>
    groups.value.filter((group) => group.isActive && group.type === 'Group'),
  )
  const scannedIds = computed(() => scanned.value.map((group) => group.id))

  const query = useQuery({
    queryKey: ['curator', 'absentees', scannedIds],
    queryFn: async ({ signal }): Promise<AbsenteeResult> => {
      const results = await Promise.all(
        scanned.value.map(async (group) => {
          try {
            return { rows: await collectForGroup(group, signal), failed: null }
          } catch {
            /*
              BITTA guruhdagi xato butun ekranni yiqitmasligi kerak: kurator
              o'nlab guruhni nazorat qiladi va bittasi arxivlangan/ruxsatsiz
              bo'lsa qolganini ko'ra olishi shart. Xato YASHIRILMAYDI —
              guruh nomi ro'yxatga tushadi va ekranda aytiladi.
            */
            return { rows: [] as AbsenteeRow[], failed: group.name ?? `Guruh #${group.id}` }
          }
        }),
      )

      return {
        rows: results.flatMap((item) => item.rows),
        failedGroups: results
          .map((item) => item.failed)
          .filter((name): name is string => name !== null),
      }
    },
    enabled: computed(() => scannedIds.value.length > 0),
    // Davomat tuzatilsa ro'yxat o'zgaradi, lekin har fokusda qayta
    // hisoblash 3N so'rov degani — bir daqiqa yetarli.
    staleTime: 60_000,
  })

  return {
    rows: computed(() => query.data.value?.rows ?? []),
    failedGroups: computed(() => query.data.value?.failedGroups ?? []),
    pending: computed(() => scannedIds.value.length > 0 && query.isPending.value),
    fetching: computed(() => query.isFetching.value),
    errorMessage: computed(() =>
      query.error.value !== null ? toUserMessage(query.error.value) : null,
    ),
    refetch: () => {
      void query.refetch()
    },
  }
}
