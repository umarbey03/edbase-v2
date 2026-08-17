export {
  fetchTeacherAvailability,
  fetchTeacherAvailabilityDetail,
  fetchTeacherAvailabilitySummary,
} from './api/teacher-availability-api'

export {
  CHECKIN_STATUS_OPTIONS,
  RANGE_PRESETS,
  checkinStatusLabel,
  checkinStatusTone,
  coverageLabel,
  coverageTone,
  daysAgoIso,
  isValidIsoDate,
  monthStartIso,
  offerStatusLabel,
  offerStatusTone,
  rangeError,
  todayIso,
} from './model/types'

export type { AvailabilityTone, RangePreset } from './model/types'
