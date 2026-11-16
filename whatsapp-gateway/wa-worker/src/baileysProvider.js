import axios from "axios";
import makeWASocket, { Browsers, DisconnectReason } from "baileys";
import { Boom } from "@hapi/boom";
import pino from "pino";
import QRCode from "qrcode";
import {
  extractInboundText,
  inboundFromPhone,
  messageTimestampToIso,
  resolveRecipientJid,
  shouldIgnoreInbound
} from "./messageUtils.js";
import { maskPhone } from "./security.js";

const TRANSIENT_RECONNECT_CODES = new Set([
  DisconnectReason.restartRequired,
  DisconnectReason.connectionClosed,
  DisconnectReason.connectionLost,
  DisconnectReason.timedOut
]);

export class BaileysProvider {
  constructor({
    sessionStore,
    gatewayInternalUrl,
    workerInternalToken,
    logger = pino({ level: "info" }),
    makeSocket = makeWASocket,
    httpClient = axios,
    reconnectBaseDelayMs = 5000,
    reconnectMaxDelayMs = 60000
  }) {
    this.sessionStore = sessionStore;
    this.gatewayInternalUrl = gatewayInternalUrl;
    this.workerInternalToken = workerInternalToken;
    this.logger = logger;
    this.makeSocket = makeSocket;
    this.httpClient = httpClient;
    this.reconnectBaseDelayMs = reconnectBaseDelayMs;
    this.reconnectMaxDelayMs = reconnectMaxDelayMs;
    this.sessions = new Map();
    this.sentNotifications = new Map();
  }

  async restoreSessions() {
    const clinicIds = await this.sessionStore.listClinicIds?.() || [];
    // One clinic with unreadable session data (corrupt file, wrong encryption key) must
    // never stop the worker from starting for every other clinic.
    await Promise.all(clinicIds.map(async (clinicId) => {
      const session = await this.#getOrCreateSession(clinicId);
      try {
        session.desiredConnected = true;
        await this.#ensureSocket(clinicId, session);
      } catch (error) {
        session.socket = null;
        session.desiredConnected = false;
        session.status = "auth_failed";
        session.qr = null;
        session.lastError = "Oturum verisi okunamadı. WhatsApp bağlantısını kesip yeniden eşleştirin.";
        this.logger.error({ clinicId, error: error?.message || String(error) }, "Session restore failed");
        return;
      }
      await this.retryPendingInbound(clinicId).catch((error) =>
        this.logger.warn({ clinicId, error: error?.message || String(error) }, "Pending inbound retry failed"));
    }));
  }

  startInboundRetry(intervalMs = 30000) {
    const timer = setInterval(() => this.retryPendingInbound().catch((error) =>
      this.logger.warn({ error: error?.message || String(error) }, "Pending inbound retry failed")), intervalMs);
    timer.unref?.();
    return timer;
  }

  async retryPendingInbound(onlyClinicId = null) {
    const clinicIds = onlyClinicId
      ? [onlyClinicId]
      : [...new Set([
          ...(await this.sessionStore.listClinicIds?.() || []),
          ...(await this.sessionStore.listPendingClinicIds?.() || [])
        ])];
    for (const clinicId of clinicIds) {
      const pending = await this.sessionStore.listPendingInbound?.(clinicId) || [];
      for (const item of pending) {
        try {
          await this.#postInbound(item.payload);
          await this.sessionStore.removePendingInbound?.(clinicId, item.file);
        } catch (error) {
          if (isPermanentInboundRejection(error)) {
            // The gateway will never accept this payload. Keeping it at the head of the
            // queue would block every later message of the clinic, so set it aside.
            this.logger.error({ clinicId, status: error.response.status }, "Inbound message rejected permanently; quarantined");
            if (this.sessionStore.quarantinePendingInbound) {
              await this.sessionStore.quarantinePendingInbound(clinicId, item.file).catch(() => undefined);
            }
            continue;
          }
          this.logger.warn({ clinicId, error: error?.message || String(error) }, "Inbound message remains queued");
          break;
        }
      }
    }
  }

  health() {
    const all = [...this.sessions.values()];
    return {
      status: "ok",
      service: "wa-worker",
      provider: "baileys",
      sessions: all.length,
      readySessions: all.filter((session) => session.status === "ready").length,
      qrSessions: all.filter((session) => session.status === "qr").length,
      authFailedSessions: all.filter((session) => session.status === "auth_failed").length,
      sessionEncryption: this.sessionStore.encryptionStatus()
    };
  }

  async connect(clinicId) {
    const session = await this.#getOrCreateSession(clinicId);
    session.desiredConnected = true;
    if (session.status === "auth_failed") {
      // Linking again after a logout must start from a clean slate, whatever the previous run left behind.
      await this.#discardAuthState(clinicId, session);
      session.status = "disconnected";
      session.lastError = null;
    }
    await this.#ensureSocket(clinicId, session);
    return this.#toPayload(clinicId, session);
  }

  async disconnect(clinicId) {
    const session = this.sessions.get(clinicId);
    const socket = session?.socket;
    if (session) {
      session.desiredConnected = false;
      session.generation += 1;
      session.socket = null;
      clearTimeout(session.reconnectTimer);
      session.reconnectTimer = null;
    }
    if (socket) {
      await socket.logout?.().catch(() => undefined);
      await socket.end?.(undefined).catch(() => undefined);
    }
    this.sessions.delete(clinicId);
    await this.sessionStore.removeClinic(clinicId);
    return {
      clinicId,
      status: "disconnected",
      qr: null,
      connectedPhone: null,
      lastSeenAt: new Date().toISOString(),
      lastError: null
    };
  }

  status(clinicId) {
    return this.#toPayload(clinicId, this.sessions.get(clinicId));
  }

  qr(clinicId) {
    return this.status(clinicId);
  }

  async send(clinicId, phoneNumber, message, notificationId) {
    const session = this.sessions.get(clinicId);
    if (!session || session.status !== "ready" || !session.socket) {
      return { statusCode: 409, body: { ok: false, errorCode: "SESSION_NOT_READY", error: "WhatsApp session is not ready." } };
    }

    if (!message) {
      return { statusCode: 400, body: { ok: false, errorCode: "INVALID_PAYLOAD", error: "message is required." } };
    }

    const idempotencyKey = `${clinicId}:${notificationId}`;
    const persistedReceipt = await this.sessionStore.getSentNotification?.(clinicId, notificationId);
    const prior = this.sentNotifications.get(idempotencyKey) || persistedReceipt?.messageId;
    if (prior) {
      this.#rememberSent(idempotencyKey, prior);
      return { statusCode: 200, body: { ok: true, messageId: prior } };
    }

    let jid;
    try {
      jid = await resolveRecipientJid(session.socket, phoneNumber);
    } catch (error) {
      const errorCode = error?.errorCode || "WORKER_TRANSIENT";
      const statusCode = errorCode === "INVALID_PHONE" ? 400 : 502;
      session.lastError = errorCode === "INVALID_PHONE" ? "Invalid phone number." : "Could not resolve WhatsApp recipient.";
      return { statusCode, body: { ok: false, errorCode, error: session.lastError } };
    }

    if (!jid) {
      return { statusCode: 200, body: { ok: false, errorCode: "NOT_REGISTERED", error: "Phone number is not registered on WhatsApp." } };
    }

    let sent;
    try {
      sent = await session.socket.sendMessage(jid, { text: message });
    } catch (error) {
      session.lastError = error?.message || "Baileys send failed.";
      this.logger.warn({ clinicId, phone: maskPhone(phoneNumber), error: session.lastError }, "Baileys send failed");
      return { statusCode: 502, body: { ok: false, errorCode: "WORKER_TRANSIENT", error: session.lastError } };
    }

    // From here on the message HAS been delivered. Nothing below may turn this into a
    // retryable error, otherwise the caller sends the same message a second time.
    session.lastSeenAt = new Date().toISOString();
    session.lastError = null;
    const messageId = sent?.key?.id || null;
    if (messageId) {
      this.#rememberSent(idempotencyKey, messageId);
      try {
        await this.sessionStore.saveSentNotification?.(clinicId, notificationId, messageId);
      } catch (error) {
        this.logger.error({ clinicId, notificationId, error: error?.message || String(error) }, "Could not persist sent receipt");
      }
    }
    try {
      await session.auth.saveMessage(sent?.key || { id: notificationId, remoteJid: jid }, sent?.message || { conversation: message });
    } catch (error) {
      this.logger.warn({ clinicId, error: error?.message || String(error) }, "Could not store sent message copy");
    }
    return { statusCode: 200, body: { ok: true, messageId } };
  }

  // In-memory copy of the on-disk receipts; bounded so a long-running worker does not grow forever.
  #rememberSent(key, messageId) {
    this.sentNotifications.delete(key);
    this.sentNotifications.set(key, messageId);
    while (this.sentNotifications.size > MAX_REMEMBERED_SENDS) {
      this.sentNotifications.delete(this.sentNotifications.keys().next().value);
    }
  }

  async #getOrCreateSession(clinicId) {
    const existing = this.sessions.get(clinicId);
    if (existing) return existing;
    const session = {
      socket: null,
      auth: null,
      status: "disconnected",
      qr: null,
      connectedPhone: null,
      lastSeenAt: null,
      lastError: null,
      reconnecting: false,
      reconnectAttempts: 0,
      reconnectTimer: null,
      desiredConnected: false,
      generation: 0,
      startPromise: null
    };
    this.sessions.set(clinicId, session);
    return session;
  }

  async #startSocket(clinicId, session) {
    const auth = await this.sessionStore.useAuthState(clinicId);
    session.auth = auth;
    session.status = "connecting";
    session.lastError = null;

    const generation = session.generation;
    const socket = this.makeSocket({
      auth: auth.state,
      logger: this.logger,
      // The worker runs on Linux. Keep the advertised client consistent with the
      // actual runtime; mismatched desktop metadata can cause WhatsApp Web to
      // reject a fresh linking attempt before it emits a QR code.
      browser: Browsers.ubuntu("Chrome"),
      markOnlineOnConnect: false,
      syncFullHistory: false,
      emitOwnEvents: false,
      qrTimeout: 60000,
      getMessage: async (key) => await auth.getMessage(key)
    });

    session.socket = socket;
    this.#wireSocketEvents(clinicId, session, socket, auth, generation);
  }

  async #ensureSocket(clinicId, session) {
    if (session.socket) return;
    if (!session.startPromise) {
      session.startPromise = this.#startSocket(clinicId, session)
        .finally(() => { session.startPromise = null; });
    }
    await session.startPromise;
  }

  #wireSocketEvents(clinicId, session, socket, auth, generation) {
    // Event handlers run detached from any caller: a rejected promise here would be an
    // unhandled rejection, which terminates the process (and every clinic's session).
    const safely = (name, handler) => async (...args) => {
      try {
        await handler(...args);
      } catch (error) {
        this.logger.error({ clinicId, event: name, error: error?.message || String(error) }, "WhatsApp event handler failed");
      }
    };

    socket.ev.on("creds.update", safely("creds.update", (...args) => auth.saveCreds(...args)));

    socket.ev.on("connection.update", safely("connection.update", async (update) => {
      if (generation !== session.generation) return;
      const { connection, lastDisconnect, qr } = update;

      if (qr) {
        session.status = "qr";
        session.qr = await QRCode.toDataURL(qr);
        session.lastError = null;
      }

      if (connection === "connecting") {
        session.status = session.status === "qr" ? "qr" : "connecting";
      }

      if (connection === "open") {
        session.status = "ready";
        session.qr = null;
        session.connectedPhone = socket.user?.id || null;
        session.lastSeenAt = new Date().toISOString();
        session.lastError = null;
        session.reconnecting = false;
        session.reconnectAttempts = 0;
        clearTimeout(session.reconnectTimer);
        session.reconnectTimer = null;
      }

      if (connection === "close") {
        await this.#handleClose(clinicId, session, lastDisconnect, generation);
      }
    }));

    socket.ev.on("messages.upsert", safely("messages.upsert", async ({ type, messages }) => {
      if (generation !== session.generation || !session.desiredConnected) return;
      if (type !== "notify") return;
      for (const message of messages || []) {
        // A failure on one message must not drop the rest of the batch.
        await this.#handleInbound(clinicId, session, message).catch((error) =>
          this.logger.error({ clinicId, error: error?.message || String(error) }, "Inbound message handling failed"));
      }
    }));
  }

  async #discardAuthState(clinicId, session) {
    session.auth = null;
    try {
      await this.sessionStore.resetAuthState?.(clinicId);
    } catch (error) {
      this.logger.error({ clinicId, error: error?.message || String(error) }, "Could not discard stale WhatsApp credentials");
    }
  }

  async #handleClose(clinicId, session, lastDisconnect, generation) {
    if (!session.desiredConnected || generation !== session.generation) return;
    const statusCode = disconnectStatusCode(lastDisconnect);
    if (statusCode === DisconnectReason.loggedOut) {
      session.socket = null;
      session.reconnecting = false;
      clearTimeout(session.reconnectTimer);
      session.reconnectTimer = null;
      session.status = "auth_failed";
      session.qr = null;
      session.connectedPhone = null;
      session.lastError = "WhatsApp session logged out.";
      await this.#discardAuthState(clinicId, session);
      return;
    }

    session.status = "disconnected";
    session.socket = null;
    session.connectedPhone = null;
    session.lastError = statusCode === 405
      ? "WhatsApp yeni bağlantı isteğini QR üretmeden reddetti (HTTP 405). Birkaç dakika bekleyip tekrar deneyin; sorun sürerse WhatsApp Web istemcisi güncellenmelidir."
      : lastDisconnect?.error?.message || "WhatsApp socket disconnected.";

    const retryable = statusCode === undefined
      || statusCode >= 500
      || TRANSIENT_RECONNECT_CODES.has(statusCode);
    this.logger.warn({ clinicId, statusCode, retryable }, "WhatsApp socket closed");
    if (!retryable || session.reconnecting) return;

    session.reconnecting = true;
    const delayMs = Math.min(
      this.reconnectBaseDelayMs * (2 ** session.reconnectAttempts),
      this.reconnectMaxDelayMs
    );
    session.reconnectAttempts += 1;
    session.reconnectTimer = setTimeout(async () => {
      session.reconnectTimer = null;
      session.reconnecting = false;
      if (!session.desiredConnected || generation !== session.generation) return;
      await this.#ensureSocket(clinicId, session).catch((error) => {
        session.lastError = error?.message || "Reconnect failed.";
        this.logger.warn({ clinicId, error: session.lastError }, "WhatsApp reconnect failed");
      });
    }, delayMs);
    session.reconnectTimer.unref?.();
  }

  async #handleInbound(clinicId, session, message) {
    if (shouldIgnoreInbound(message)) return;
    const text = extractInboundText(message);
    // A blank text (only spaces/newlines) carries nothing to store or act on.
    if (!text || !text.trim()) return;

    const payload = {
        clinic_id: clinicId,
        from_phone: String(inboundFromPhone(message) ?? "").slice(0, MAX_FROM_PHONE_LENGTH),
        // WhatsApp texts can be longer than the gateway accepts. Truncating keeps the
        // message deliverable instead of being rejected (422) on every retry.
        message: truncateByCodePoints(text, MAX_INBOUND_TEXT_LENGTH),
        received_at: messageTimestampToIso(message),
        gateway_session_id: clinicId,
        provider_message_id: String(message?.key?.id || `${message?.key?.remoteJid || "unknown"}:${message?.messageTimestamp || "unknown"}`).slice(0, 256)
    };
    try {
      await this.#postInbound(payload);
    } catch (error) {
      session.lastError = error?.message || String(error);
      try {
        await this.sessionStore.savePendingInbound?.(clinicId, payload);
      } catch (storeError) {
        this.logger.error({ clinicId, error: storeError?.message || String(storeError) }, "Could not queue inbound message");
      }
    }
  }

  async #postInbound(payload) {
    await this.httpClient.post(
      `${this.gatewayInternalUrl}/internal/worker/inbound`, payload,
      { headers: { "x-internal-token": this.workerInternalToken }, timeout: INBOUND_POST_TIMEOUT_MS }
    );
  }

  #toPayload(clinicId, session) {
    return {
      clinicId,
      status: session?.status || "disconnected",
      qr: session?.qr || null,
      connectedPhone: session?.connectedPhone || null,
      lastSeenAt: session?.lastSeenAt || null,
      lastError: session?.lastError || null
    };
  }
}

const MAX_REMEMBERED_SENDS = 5000;
const MAX_INBOUND_TEXT_LENGTH = 4096;
const MAX_FROM_PHONE_LENGTH = 32;
const INBOUND_POST_TIMEOUT_MS = 15000;

// Only answers that say "this payload is invalid" are permanent. 404/405 (wrong route during a
// deploy or proxy change), 401/403 (token) and 408/425/429 describe the environment, not the
// message: those must stay queued and be retried once the environment is fixed.
const PAYLOAD_REJECTIONS = new Set([400, 413, 422]);

// String.slice counts UTF-16 units and can cut an emoji in half; the lone surrogate then
// cannot be encoded as UTF-8 by the gateway and the message would be stuck in the queue.
export function truncateByCodePoints(text, max) {
  const points = Array.from(String(text));
  return points.length <= max ? String(text) : points.slice(0, max).join("");
}

function isPermanentInboundRejection(error) {
  return PAYLOAD_REJECTIONS.has(error?.response?.status);
}

function disconnectStatusCode(lastDisconnect) {
  const error = lastDisconnect?.error;
  if (!error) return undefined;
  if (error instanceof Boom) return error.output?.statusCode;
  return error?.output?.statusCode;
}
