export { fetchSessionAttendance, updateAttendance } from './api/attendance-api'
export {
  ATTENDANCE_CHOICES,
  ATTENDANCE_REASON_MAX,
  attendanceStatusLabel,
  attendanceStatusTone,
  attendanceSymbol,
  durationLabel,
} from './model/types'
export type {
  AttendanceRowDto,
  AttendanceStatusName,
  AttendanceTone,
  SessionAttendanceDto,
  UpdateAttendanceRequest,
} from './model/types'
