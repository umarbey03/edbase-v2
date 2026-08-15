export {
  // ⚠️ `devQuickLogin` / `fetchDevQuickLoginAccounts` — FAQAT SINOV
  //    UCHUN (kirish sahifasidagi rol tugmalari). Ular haqiqiy kirish
  //    oqimiga aloqador emas — batafsil `auth-api.ts` oxiridagi izohda.
  devQuickLogin,
  fetchDevQuickLoginAccounts,
  fetchMe,
  loginWithTelegram,
  logout,
  requestPhoneCode,
  verifyPhoneCode,
} from './api/auth-api'
export { dropAvatar, useAvatar } from './model/useAvatar'
export {
  avatarPath,
  cancelPhoneChange,
  confirmPhoneChange,
  fetchAvatarBlob,
  fetchPhoneChange,
  removeAvatar,
  requestPhoneChange,
  updateProfileName,
  uploadAvatar,
} from './api/profile-api'
export {
  createStudentNote,
  deleteStudentNote,
  NOTE_BODY_MAX,
  updateStudentNote,
} from './api/student-note-api'
export {
  activateUser,
  createUser,
  deactivateUser,
  fetchUsers,
  updateUser,
  USER_SEARCH_MIN,
} from './api/user-api'
export type { UserListParams } from './api/user-api'
export { fetchUserProfile, unlinkTelegram } from './api/user-profile-api'
export { homeRouteFor, navItemsForRole } from './model/navigation'
export type { NavItem } from './model/navigation'
export {
  attendanceTone,
  percentLabel,
  TELEGRAM_FILTER_OPTIONS,
  telegramFilterToParam,
  telegramHandle,
  telegramLink,
  UNLINK_REASON_MAX,
} from './model/profile'
export type { TelegramFilterValue } from './model/profile'
export {
  canSeeStudentContact,
  isAdminRole,
  isManagerRole,
  isStaffRole,
  ROLE_OPTIONS,
  roleLabel,
  roleTone,
  roleWeight,
} from './model/types'
export type { RoleTone, User } from './model/types'
