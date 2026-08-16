import { http } from '@/shared/api'
import type {
  CreateMessageTemplateRequest,
  MessageTemplateDto,
  UpdateMessageTemplateRequest,
} from '@/shared/types'

const BASE = '/api/v1/message-templates'

/**
 * XABAR SHABLONLARI (2026-08-16) — "Xabarlar" panelining tanlagichini
 * to'ldiradigan lug'at, Sozlamalar bo'limidan boshqariladi.
 *
 * ★ RUXSAT: server bu yo'lni FAQAT `Academic,Admin` bilan ochgan (o'qish
 * ham, yozish ham) — `fetchGroupCategories`dan farqli, chunki bu ICHKI
 * xabar vositasi, ustoz/kurator uni ko'rmaydi/ishlatmaydi.
 */

export interface MessageTemplateListParams {
  /** `true` — faqat faollar. Berilmasa hammasi (Sozlamalar arxivlanganini ham ko'rsatadi). */
  isActive?: boolean
}

export function fetchMessageTemplates(
  params: MessageTemplateListParams = {},
  options?: { signal?: AbortSignal },
): Promise<MessageTemplateDto[]> {
  return http.get<MessageTemplateDto[]>(BASE, {
    query: { IsActive: params.isActive },
    signal: options?.signal,
  })
}

export function createMessageTemplate(
  body: CreateMessageTemplateRequest,
): Promise<MessageTemplateDto> {
  return http.post<MessageTemplateDto>(BASE, body)
}

export function updateMessageTemplate(
  id: number,
  body: UpdateMessageTemplateRequest,
): Promise<MessageTemplateDto> {
  return http.put<MessageTemplateDto>(`${BASE}/${id}`, body)
}

export function deleteMessageTemplate(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/${id}`)
}
