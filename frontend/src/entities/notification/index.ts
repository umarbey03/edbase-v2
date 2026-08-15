export {
  deleteNotifications,
  fetchNotifications,
  fetchUnreadCount,
  markNotificationsRead,
} from './api/notification-api'
export type { NotificationPageParams } from './api/notification-api'
export {
  badgeLabel,
  mergeNotifications,
  NOTIFICATION_BADGE_MAX,
  NOTIFICATION_PAGE_SIZE,
  notificationIcon,
  notificationRouteName,
} from './model/types'
