import { EventEmitter } from "node:events";
import { describe, expect, it, vi } from "vitest";
import { BaileysProvider } from "../src/baileysProvider.js";

const clinicA = "73ccade0-16f3-4d2d-b55d-fe152ae4d05b";
const clinicB = "cb8c30a7-d623-48cf-aa38-79d630b22389";

describe("BaileysProvider", () => {
  it("tracks QR and ready lifecycle per clinic", async () => {
    const { provider, sockets } = createProvider();

    await provider.connect(clinicA);
    sockets[0].emitConnection({ qr: "qr-payload" });
    await waitFor(() => provider.status(clinicA).qr);
    expect(provider.status(clinicA).status).toBe("qr");
    expect(provider.status(clinicA).qr).toContain("data:image/png;base64");

    sockets[0].user = { id: "905550000000@s.whatsapp.net" };
    sockets[0].emitConnection({ connection: "open" });
    await flush();

    const status = provider.status(clinicA);
    expect(status.status).toBe("ready");
    expect(status.qr).toBeNull();
    expect(status.connectedPhone).toBe("905550000000@s.whatsapp.net");
  });

  it("disconnects only the selected clinic session", async () => {
    const { provider, sockets } = createProvider();

    await provider.connect(clinicA);
    await provider.connect(clinicB);
    sockets[0].emitConnection({ connection: "open" });
    sockets[1].emitConnection({ connection: "open" });
    await provider.disconnect(clinicA);

    expect(provider.status(clinicA).status).toBe("disconnected");
    expect(provider.status(clinicB).status).toBe("ready");
    expect(sockets[0].logout).toHaveBeenCalled();
    expect(sockets[1].logout).not.toHaveBeenCalled();
  });

  it("sends text messages through the clinic socket only", async () => {
    const { provider, sockets } = createProvider();
    await provider.connect(clinicA);
    sockets[0].emitConnection({ connection: "open" });
    sockets[0].onWhatsApp.mockResolvedValue([{ exists: true, jid: "905551111111@s.whatsapp.net" }]);
    sockets[0].sendMessage.mockResolvedValue({ key: { id: "msg-1", remoteJid: "905551111111@s.whatsapp.net" }, message: { conversation: "hello" } });

    const result = await provider.send(clinicA, "+90 555 111 11 11", "hello", "notif-1");

    expect(result.statusCode).toBe(200);
    expect(result.body).toEqual({ ok: true, messageId: "msg-1" });
    expect(sockets[0].sendMessage).toHaveBeenCalledWith("905551111111@s.whatsapp.net", { text: "hello" });

    const duplicate = await provider.send(clinicA, "+90 555 111 11 11", "hello", "notif-1");
    expect(duplicate.body).toEqual({ ok: true, messageId: "msg-1" });
    expect(sockets[0].sendMessage).toHaveBeenCalledTimes(1);
  });

  it("returns permanent validation errors without retryable worker failures", async () => {
    const { provider, sockets } = createProvider();
    await provider.connect(clinicA);
    sockets[0].emitConnection({ connection: "open" });

    const invalidPhone = await provider.send(clinicA, "123", "hello", "notif-1");
    expect(invalidPhone.statusCode).toBe(400);
    expect(invalidPhone.body.errorCode).toBe("INVALID_PHONE");

    sockets[0].onWhatsApp.mockResolvedValue([]);
    const notRegistered = await provider.send(clinicA, "+905551111111", "hello", "notif-1");
    expect(notRegistered.statusCode).toBe(200);
    expect(notRegistered.body.errorCode).toBe("NOT_REGISTERED");
  });

  it("maps Baileys send failures to WORKER_TRANSIENT", async () => {
    const { provider, sockets } = createProvider();
    await provider.connect(clinicA);
    sockets[0].emitConnection({ connection: "open" });
    sockets[0].onWhatsApp.mockResolvedValue([{ exists: true, jid: "905551111111@s.whatsapp.net" }]);
    sockets[0].sendMessage.mockRejectedValue(new Error("socket timeout"));

    const result = await provider.send(clinicA, "+905551111111", "hello", "notif-1");

    expect(result.statusCode).toBe(502);
    expect(result.body.errorCode).toBe("WORKER_TRANSIENT");
  });

  it("forwards only supported inbound direct messages", async () => {
    const httpClient = { post: vi.fn().mockResolvedValue({}) };
    const { provider, sockets } = createProvider({ httpClient });
    await provider.connect(clinicA);

    sockets[0].emitMessages({
      type: "notify",
      messages: [
        {
          key: { fromMe: true, remoteJid: "905551111111@s.whatsapp.net" },
          message: { conversation: "from me" },
          messageTimestamp: 1
        },
        {
          key: { fromMe: false, remoteJid: "12345@g.us" },
          message: { conversation: "group" },
          messageTimestamp: 1
        },
        {
          key: { fromMe: false, remoteJid: "905551111111@s.whatsapp.net" },
          message: { extendedTextMessage: { text: "Merhaba" } },
          messageTimestamp: 1
        },
        {
          key: { fromMe: false, remoteJid: "905552222222@s.whatsapp.net" },
          message: { imageMessage: {} },
          messageTimestamp: 1
        }
      ]
    });
    await flush();

    expect(httpClient.post).toHaveBeenCalledTimes(2);
    expect(httpClient.post.mock.calls[0][1].message).toBe("Merhaba");
    expect(httpClient.post.mock.calls[1][1].message).toBe("[media:image]");
  });

  it("persists failed inbound delivery and retries it after provider restart", async () => {
    const queued = [];
    const sessionStore = {
      encryptionStatus: () => "enabled",
      listClinicIds: vi.fn(async () => []),
      listPendingClinicIds: vi.fn(async () => [clinicA]),
      listPendingInbound: vi.fn(async () => queued.map((payload, index) => ({ file: `${index}.json`, payload }))),
      savePendingInbound: vi.fn(async (_clinicId, payload) => queued.push(payload)),
      removePendingInbound: vi.fn(async () => queued.shift()),
      useAuthState: vi.fn(async () => ({
        state: { creds: {}, keys: { get: vi.fn(), set: vi.fn() } },
        saveCreds: vi.fn(), getMessage: vi.fn(), saveMessage: vi.fn()
      })),
      removeClinic: vi.fn()
    };
    const failingHttp = { post: vi.fn().mockRejectedValue(new Error("gateway offline")) };
    const first = createProvider({ sessionStore, httpClient: failingHttp });
    await first.provider.connect(clinicA);
    first.sockets[0].emitMessages({
      type: "notify",
      messages: [{
        key: { id: "inbound-1", fromMe: false, remoteJid: "905551111111@s.whatsapp.net" },
        message: { conversation: "Kalıcı mesaj" },
        messageTimestamp: 1
      }]
    });
    await waitFor(() => queued.length === 1);

    const succeedingHttp = { post: vi.fn().mockResolvedValue({}) };
    const restarted = createProvider({ sessionStore, httpClient: succeedingHttp });
    await restarted.provider.retryPendingInbound();

    expect(succeedingHttp.post).toHaveBeenCalledTimes(1);
    expect(succeedingHttp.post.mock.calls[0][1].provider_message_id).toBe("inbound-1");
    expect(queued).toHaveLength(0);
  });
});

function createProvider(overrides = {}) {
  const sockets = [];
  const sessionStore = overrides.sessionStore || {
    encryptionStatus: () => "disabled",
    useAuthState: vi.fn(async () => ({
      state: { creds: {}, keys: { get: vi.fn(), set: vi.fn() } },
      saveCreds: vi.fn(),
      getMessage: vi.fn(),
      saveMessage: vi.fn()
    })),
    removeClinic: vi.fn()
  };
  const provider = new BaileysProvider({
    sessionStore,
    gatewayInternalUrl: "http://gateway-api:8088",
    workerInternalToken: "test-token",
    logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn(), child: () => ({ info: vi.fn(), warn: vi.fn(), error: vi.fn() }) },
    httpClient: overrides.httpClient || { post: vi.fn().mockResolvedValue({}) },
    makeSocket: () => {
      const socket = createSocketMock();
      sockets.push(socket);
      return socket;
    }
  });
  return { provider, sockets, sessionStore };
}

function createSocketMock() {
  const ev = new EventEmitter();
  return {
    ev,
    user: null,
    onWhatsApp: vi.fn(),
    sendMessage: vi.fn(),
    logout: vi.fn().mockResolvedValue(undefined),
    end: vi.fn().mockResolvedValue(undefined),
    emitConnection: (payload) => ev.emit("connection.update", payload),
    emitMessages: (payload) => ev.emit("messages.upsert", payload)
  };
}

async function flush() {
  await new Promise((resolve) => setTimeout(resolve, 0));
}

async function waitFor(predicate) {
  for (let index = 0; index < 20; index += 1) {
    if (predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
}
