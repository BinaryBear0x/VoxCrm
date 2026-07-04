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
    if (!session.socket) {
      await this.#startSocket(clinicId, session);
    }
    return this.#toPayload(clinicId, session);
  }

  async disconnect(clinicId) {
    const session = this.sessions.get(clinicId);
    if (session?.socket) {
      await session.socket.logout?.().catch(() => undefined);
      await session.socket.end?.(undefined).catch(() => undefined);
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
      return { statusCode: 200, body: { ok: true, messageId: sent?.key?.id || null } };
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
      reconnecting: false
    };
    this.sessions.set(clinicId, session);
    return session;
  }

  async #startSocket(clinicId, session) {
    const auth = await this.sessionStore.useAuthState(clinicId);
    session.auth = auth;
    session.status = "connecting";
    session.lastError = null;

    const socket = this.makeSocket({
      auth: auth.state,
      logger: this.logger,
      browser: Browsers.macOS("Chrome"),
      markOnlineOnConnect: false,
      qrTimeout: 60000,
      getMessage: async (key) => await auth.getMessage(key)
    });

    session.socket = socket;
    this.#wireSocketEvents(clinicId, session, socket, auth);
  }

  #wireSocketEvents(clinicId, session, socket, auth) {
    socket.ev.on("creds.update", auth.saveCreds);

    socket.ev.on("connection.update", async (update) => {
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
        await this.#handleClose(clinicId, session, lastDisconnect);
      }
    });

    socket.ev.on("messages.upsert", async ({ type, messages }) => {
      if (type !== "notify") return;
      for (const message of messages || []) {
        await this.#handleInbound(clinicId, session, message);
      }
    });
  }

  async #handleClose(clinicId, session, lastDisconnect) {
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
      await this.#startSocket(clinicId, session).catch((error) => {
        session.reconnecting = false;
        session.lastError = error?.message || "Reconnect failed.";
      });
    }
  }

  async #handleInbound(clinicId, session, message) {
    if (shouldIgnoreInbound(message)) return;
    const text = extractInboundText(message);
    if (!text) return;

    await this.httpClient.post(
      `${this.gatewayInternalUrl}/internal/worker/inbound`,
      {
        clinic_id: clinicId,
        from_phone: inboundFromPhone(message),
        message: text,
        received_at: messageTimestampToIso(message),
        gateway_session_id: clinicId
      },
      { headers: { "x-internal-token": this.workerInternalToken } }
    ).catch((error) => {
      session.lastError = error?.message || String(error);
    });
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
