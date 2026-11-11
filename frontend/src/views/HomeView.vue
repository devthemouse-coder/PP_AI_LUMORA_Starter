<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  getConversationMessages,
  getConversations,
  sendChatTurn,
  type ChatTurnResponse,
  type ConversationMessage,
  type ConversationSummary,
} from '@/services/api'

const { t, locale } = useI18n()
const conversations = ref<ConversationSummary[]>([])
const messages = ref<ConversationMessage[]>([])
const conversationId = ref<string | null>(null)
const messageInput = ref('')
const isSending = ref(false)
const isLoadingHistory = ref(false)
const errorMessage = ref('')
const pendingMessage = ref('')

onMounted(() => {
  void refreshConversations()
})

async function refreshConversations(): Promise<void> {
  try {
    conversations.value = await getConversations(AbortSignal.timeout(10_000))
  } catch {
    errorMessage.value = t('chat.historyUnavailable')
  }
}

async function openConversation(id: string): Promise<void> {
  if (isSending.value) return
  isLoadingHistory.value = true
  errorMessage.value = ''
  try {
    messages.value = await getConversationMessages(id, AbortSignal.timeout(10_000))
    conversationId.value = id
  } catch {
    errorMessage.value = t('chat.historyUnavailable')
  } finally {
    isLoadingHistory.value = false
  }
}

function startNewConversation(): void {
  if (isSending.value || isLoadingHistory.value) return
  conversationId.value = null
  messages.value = []
  errorMessage.value = ''
  messageInput.value = ''
  pendingMessage.value = ''
}

async function sendMessage(): Promise<void> {
  const message = messageInput.value.trim()
  if (!message || isSending.value) return

  errorMessage.value = ''
  isSending.value = true
  messageInput.value = ''
  pendingMessage.value = message

  let result: ChatTurnResponse
  try {
    result = await sendChatTurn(
      message,
      locale.value === 'en' ? 'en' : 'ko',
      conversationId.value,
      AbortSignal.timeout(70_000),
    )
  } catch {
    errorMessage.value = t('chat.sendFailed')
    messageInput.value = message
    pendingMessage.value = ''
    isSending.value = false
    return
  }

  conversationId.value = result.conversationId
  const fallbackMessages: ConversationMessage[] = [
    {
      id: crypto.randomUUID(),
      role: 'User',
      content: message,
      createdAtUtc: new Date().toISOString(),
    },
    {
      id: crypto.randomUUID(),
      role: 'Assistant',
      content: result.response,
      createdAtUtc: new Date().toISOString(),
    },
  ]
  pendingMessage.value = ''

  if (result.conversationId) {
    try {
      messages.value = await getConversationMessages(
        result.conversationId,
        AbortSignal.timeout(10_000),
      )
    } catch {
      messages.value = [...messages.value, ...fallbackMessages]
      errorMessage.value = t('chat.historyUnavailable')
    }
    await refreshConversations()
  } else {
    messages.value = [...messages.value, ...fallbackMessages]
  }

  isSending.value = false
}
</script>

<template>
  <main class="home">
    <h1>{{ t('app.name') }}</h1>
    <p>{{ t('app.description') }}</p>
    <section class="chat-layout" :aria-busy="isSending || isLoadingHistory">
      <aside class="conversation-list" :aria-label="t('chat.history')">
        <button
          type="button"
          class="new-conversation"
          :disabled="isSending || isLoadingHistory"
          @click="startNewConversation"
        >
          {{ t('chat.newConversation') }}
        </button>
        <button
          v-for="conversation in conversations"
          :key="conversation.id"
          type="button"
          class="conversation-item"
          :aria-current="conversation.id === conversationId ? 'page' : undefined"
          :disabled="isSending || isLoadingHistory"
          @click="openConversation(conversation.id)"
        >
          {{ new Date(conversation.lastMessageAtUtc).toLocaleString(locale) }}
        </button>
      </aside>

      <section class="chat-panel" :aria-label="t('chat.title')">
        <div class="message-list" aria-live="polite">
          <p v-if="messages.length === 0" class="empty-state">{{ t('chat.empty') }}</p>
          <article
            v-for="message in messages"
            :key="message.id"
            class="message"
            :class="`message-${message.role.toLowerCase()}`"
          >
            <p>{{ message.content }}</p>
          </article>
          <article v-if="pendingMessage" class="message message-user">
            <p>{{ pendingMessage }}</p>
          </article>
          <p v-if="isSending" class="processing">{{ t('chat.processing') }}</p>
        </div>

        <p v-if="errorMessage" class="error-message" role="alert">{{ errorMessage }}</p>
        <form class="chat-form" @submit.prevent="sendMessage">
          <label class="sr-only" for="chat-message">{{ t('chat.messageLabel') }}</label>
          <textarea
            id="chat-message"
            v-model="messageInput"
            maxlength="2000"
            rows="3"
            :placeholder="t('chat.placeholder')"
            :disabled="isSending"
            @keydown.enter.exact.prevent="sendMessage"
          />
          <button type="submit" :disabled="isSending || !messageInput.trim()">
            {{ t('chat.send') }}
          </button>
        </form>
      </section>
    </section>
  </main>
</template>

<style scoped>
.home {
  max-width: 1100px;
  margin: 0 auto;
  padding: 2rem;
}

.chat-layout {
  display: grid;
  grid-template-columns: minmax(180px, 250px) 1fr;
  min-height: 65vh;
  border: 1px solid #d5d9df;
  border-radius: 12px;
  overflow: hidden;
}

.conversation-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1rem;
  background: #f5f7fa;
  border-right: 1px solid #d5d9df;
}

.conversation-item,
.new-conversation,
.chat-form button {
  padding: 0.65rem 0.8rem;
  border: 1px solid #b8c0cc;
  border-radius: 8px;
  background: white;
  color: inherit;
  cursor: pointer;
  text-align: left;
}

.chat-panel {
  display: flex;
  flex-direction: column;
  min-width: 0;
  padding: 1rem;
}

.message-list {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 0.75rem;
  overflow-y: auto;
  padding-bottom: 1rem;
}

.message {
  max-width: 85%;
  padding: 0.25rem 0.9rem;
  border-radius: 10px;
  overflow-wrap: anywhere;
}

.message p {
  white-space: pre-wrap;
}

.message-user {
  align-self: flex-end;
  background: #e4efff;
}

.message-assistant {
  align-self: flex-start;
  background: #f1f2f4;
}

.empty-state,
.processing {
  color: #5d6673;
}

.error-message {
  color: #a32121;
}

.chat-form {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
}

.chat-form textarea {
  flex: 1;
  resize: vertical;
  padding: 0.75rem;
  border: 1px solid #b8c0cc;
  border-radius: 8px;
  font: inherit;
}

.chat-form button {
  text-align: center;
}

.chat-form button:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

@media (max-width: 700px) {
  .home {
    padding: 1rem;
  }

  .chat-layout {
    grid-template-columns: 1fr;
  }

  .conversation-list {
    max-height: 180px;
    overflow-y: auto;
    border-right: 0;
    border-bottom: 1px solid #d5d9df;
  }
}
</style>
