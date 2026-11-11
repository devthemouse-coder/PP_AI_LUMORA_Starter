import type { HealthCheckResponse } from '@/types/health'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL

export interface ChatTurnResponse {
  conversationId: string | null
  response: string
  persisted: boolean
}

export interface ConversationSummary {
  id: string
  createdAtUtc: string
  updatedAtUtc: string
  lastMessageAtUtc: string
}

export interface ConversationMessage {
  id: string
  role: 'User' | 'Assistant'
  content: string
  createdAtUtc: string
}

interface PageResponse<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  totalCount: number
}

export async function getApiHealth(signal?: AbortSignal): Promise<HealthCheckResponse> {
  if (!apiBaseUrl) {
    throw new Error('VITE_API_BASE_URL is not configured.')
  }

  const response = await fetch(`${apiBaseUrl.replace(/\/$/, '')}/health`, {
    headers: {
      Accept: 'application/json',
    },
    signal,
  })

  if (!response.ok) {
    throw new Error(`Health check failed with HTTP ${response.status}.`)
  }

  const payload: unknown = await response.json()
  if (!isHealthCheckResponse(payload)) {
    throw new TypeError('The health check response has an invalid shape.')
  }

  return payload
}

export async function sendChatTurn(
  message: string,
  language: string,
  conversationId: string | null,
  signal?: AbortSignal,
): Promise<ChatTurnResponse> {
  const response = await apiFetch('/chat', {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ message, language, conversationId }),
    signal,
  })

  if (!response.ok) {
    throw new Error(`Chat request failed with HTTP ${response.status}.`)
  }

  const payload: unknown = await response.json()
  if (!isChatTurnResponse(payload)) {
    throw new TypeError('The chat response has an invalid shape.')
  }

  return payload
}

export async function getConversations(signal?: AbortSignal): Promise<ConversationSummary[]> {
  const response = await apiFetch('/conversations?pageNumber=1&pageSize=50', {
    headers: { Accept: 'application/json' },
    signal,
  })
  if (!response.ok) {
    throw new Error(`Conversation request failed with HTTP ${response.status}.`)
  }

  const payload: unknown = await response.json()
  if (!isPageResponse(payload, isConversationSummary)) {
    throw new TypeError('The conversation response has an invalid shape.')
  }

  return payload.items
}

export async function getConversationMessages(
  conversationId: string,
  signal?: AbortSignal,
): Promise<ConversationMessage[]> {
  const response = await apiFetch(
    `/conversations/${encodeURIComponent(conversationId)}/messages?pageNumber=1&pageSize=100`,
    {
      headers: { Accept: 'application/json' },
      signal,
    },
  )
  if (!response.ok) {
    throw new Error(`Conversation messages request failed with HTTP ${response.status}.`)
  }

  const payload: unknown = await response.json()
  if (!isPageResponse(payload, isConversationMessage)) {
    throw new TypeError('The conversation messages response has an invalid shape.')
  }

  return payload.items
}

function apiFetch(path: string, init: RequestInit): Promise<Response> {
  if (!apiBaseUrl) {
    throw new Error('VITE_API_BASE_URL is not configured.')
  }

  return fetch(`${apiBaseUrl.replace(/\/$/, '')}${path}`, init)
}

function isHealthCheckResponse(value: unknown): value is HealthCheckResponse {
  if (typeof value !== 'object' || value === null) {
    return false
  }

  if (!('status' in value) || !('applicationName' in value) || !('serverTime' in value)) {
    return false
  }

  return (
    value.status === 'ok' &&
    typeof value.applicationName === 'string' &&
    typeof value.serverTime === 'string' &&
    !Number.isNaN(Date.parse(value.serverTime))
  )
}

function isChatTurnResponse(value: unknown): value is ChatTurnResponse {
  return (
    isObject(value) &&
    (value.conversationId === null || typeof value.conversationId === 'string') &&
    typeof value.response === 'string' &&
    typeof value.persisted === 'boolean'
  )
}

function isConversationSummary(value: unknown): value is ConversationSummary {
  return (
    isObject(value) &&
    typeof value.id === 'string' &&
    typeof value.createdAtUtc === 'string' &&
    typeof value.updatedAtUtc === 'string' &&
    typeof value.lastMessageAtUtc === 'string'
  )
}

function isConversationMessage(value: unknown): value is ConversationMessage {
  return (
    isObject(value) &&
    typeof value.id === 'string' &&
    (value.role === 'User' || value.role === 'Assistant') &&
    typeof value.content === 'string' &&
    typeof value.createdAtUtc === 'string'
  )
}

function isPageResponse<T>(
  value: unknown,
  isItem: (item: unknown) => item is T,
): value is PageResponse<T> {
  return (
    isObject(value) &&
    Array.isArray(value.items) &&
    value.items.every(isItem) &&
    Number.isInteger(value.pageNumber) &&
    Number.isInteger(value.pageSize) &&
    Number.isInteger(value.totalCount)
  )
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}
