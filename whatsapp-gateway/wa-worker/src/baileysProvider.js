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
    httpClient = axios
  }) {
    this.sessionStore = sessionStore;
    this.gatewayInternalUrl = gatewayInternalUrl;
    this.workerInternalToken = workerInternalToken;
    this.logger = logger;
    this.makeSocket = makeSocket;
    this.httpClient = httpClient;
    this.sessions = new Map();
    this.sentNotifications = new Map();
  }

  async restoreSessions() {
    const clinicIds = await this.sessionStore.listClinicIds?.() || [];
    await Promise.all(clinicIds.map(async (clinicId) => {
      const session = await this.#getOrCreateSession(clinicId);
      session.desiredConnected = true;
      await this.#ensureSocket(clinicId, session);
      await this.retryPendingInbound(clinicId);
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
      this.sentNotifications.set(idempotencyKey, prior);
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

    try {
      const sent = await session.socket.sendMessage(jid, { text: message });
      await session.auth.saveMessage(sent?.key || { id: notificationId, remoteJid: jid }, sent?.message || { conversation: message });
      session.lastSeenAt = new Date().toISOString();
      session.lastError = null;
      const messageId = sent?.key?.id || null;
      if (messageId) {
        await this.sessionStore.saveSentNotification?.(clinicId, notificationId, messageId);
        this.sentNotifications.set(idempotencyKey, messageId);
      }
      return { statusCode: 200, body: { ok: true, messageId } };
    } catch (error) {
      session.lastError = error?.message || "Baileys send failed.";
      this.logger.warn({ clinicId, phone: maskPhone(phoneNumber), error: session.lastError }, "Baileys send failed");
      return { statusCode: 502, body: { ok: false, errorCode: "WORKER_TRANSIENT", error: session.lastError } };
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
      browser: Browsers.macOS("Chrome"),
      markOnlineOnConnect: false,
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
    socket.ev.on("creds.update", auth.saveCreds);

    socket.ev.on("connection.update", async (update) => {
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
      }

      if (connection === "close") {
        await this.#handleClose(clinicId, session, lastDisconnect, generation);
      }
    });

    socket.ev.on("messages.upsert", async ({ type, messages }) => {
      if (generation !== session.generation || !session.desiredConnected) return;
      if (type !== "notify") return;
      for (const message of messages || []) {
        await this.#handleInbound(clinicId, session, message);
      }
    });
  }

  async #handleClose(clinicId, session, lastDisconnect, generation) {
    if (!session.desiredConnected || generation !== session.generation) return;
    const statusCode = disconnectStatusCode(lastDisconnect);
    if (statusCode === DisconnectReason.loggedOut) {
      session.status = "auth_failed";
      session.qr = null;
      session.connectedPhone = null;
      session.lastError = "WhatsApp session logged out.";
      return;
    }

    session.status = "disconnected";
    session.connectedPhone = null;
    session.lastError = lastDisconnect?.error?.message || "WhatsApp socket disconnected.";

    if (!session.reconnecting && TRANSIENT_RECONNECT_CODES.has(statusCode)) {
      session.reconnecting = true;
      session.socket = null;
      await this.#ensureSocket(clinicId, session).catch((error) => {
        session.reconnecting = false;
        session.lastError = error?.message || "Reconnect failed.";
      });
    }
  }

  async #handleInbound(clinicId, session, message) {
    if (shouldIgnoreInbound(message)) return;
    const text = extractInboundText(message);
    if (!text) return;

    const payload = {
        clinic_id: clinicId,
        from_phone: inboundFromPhone(message),
        message: text,
        received_at: messageTimestampToIso(message),
        gateway_session_id: clinicId,
        provider_message_id: message?.key?.id || `${message?.key?.remoteJid || "unknown"}:${message?.messageTimestamp || "unknown"}`
    };
    await this.#postInbound(payload).catch(async (error) => {
      session.lastError = error?.message || String(error);
      await this.sessionStore.savePendingInbound?.(clinicId, payload);
    });
  }

  async #postInbound(payload) {
    await this.httpClient.post(
      `${this.gatewayInternalUrl}/internal/worker/inbound`, payload,
      { headers: { "x-internal-token": this.workerInternalToken } }
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

function disconnectStatusCode(lastDisconnect) {
  const error = lastDisconnect?.error;
  if (!error) return undefined;
  if (error instanceof Boom) return error.output?.statusCode;
  return error?.output?.statusCode;
}
