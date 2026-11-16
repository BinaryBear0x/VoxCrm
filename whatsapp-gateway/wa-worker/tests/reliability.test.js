import { EventEmitter } from "node:events";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { BaileysProvider } from "../src/baileysProvider.js";
import { FileSessionStore } from "../src/sessionStore.js";

const clinicA = "73ccade0-16f3-4d2d-b55d-fe152ae4d05b";
const clinicB = "cb8c30a7-d623-48cf-aa38-79d630b22389";

function authState(overrides = {}) {
  return {
    state: { creds: {}, keys: { get: vi.fn(), set: vi.fn() } },
    saveCreds: vi.fn(),
    getMessage: vi.fn(),
    saveMessage: vi.fn(),
    ...overrides
  };
}

function storeWith(extra = {}) {
  return {
    encryptionStatus: () => "disabled",
    useAuthState: vi.fn(async () => authState()),
    removeClinic: vi.fn(),
    ...extra
  };
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

function createProvider({ sessionStore = storeWith(), httpClient = { post: vi.fn().mockResolvedValue({}) } } = {}) {
  const sockets = [];
  const provider = new BaileysProvider({
    sessionStore,
    gatewayInternalUrl: "http://gateway-api:8088",
    workerInternalToken: "test-token",
    logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn() },
    httpClient,
    makeSocket: () => {
      const socket = createSocketMock();
      sockets.push(socket);
      return socket;
    }
  });
  return { provider, sockets };
}

async function readyProvider(options) {
  const ctx = createProvider(options);
  await ctx.provider.connect(clinicA);
  ctx.sockets[0].emitConnection({ connection: "open" });
  ctx.sockets[0].onWhatsApp.mockResolvedValue([{ exists: true, jid: "905551111111@s.whatsapp.net" }]);
  ctx.sockets[0].sendMessage.mockResolvedValue({
    key: { id: "msg-1", remoteJid: "905551111111@s.whatsapp.net" },
    message: { conversation: "hello" }
  });
  return ctx;
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe("sending", () => {
  it("still reports success when storing the sent message copy fails, so the caller does not send it twice", async () => {
    const sessionStore = storeWith({
      useAuthState: vi.fn(async () => authState({ saveMessage: vi.fn().mockRejectedValue(new Error("disk full")) }))
    });
    const { provider, sockets } = await readyProvider({ sessionStore });

    const result = await provider.send(clinicA, "+905551111111", "hello", "notif-1");

    expect(result).toEqual({ statusCode: 200, body: { ok: true, messageId: "msg-1" } });
    const again = await provider.send(clinicA, "+905551111111", "hello", "notif-1");
    expect(again.body).toEqual({ ok: true, messageId: "msg-1" });
    expect(sockets[0].sendMessage).toHaveBeenCalledTimes(1);
  });

  it("still reports success when the sent receipt cannot be persisted", async () => {
    const sessionStore = storeWith({ saveSentNotification: vi.fn().mockRejectedValue(new Error("read-only fs")) });
    const { provider, sockets } = await readyProvider({ sessionStore });

    const result = await provider.send(clinicA, "+905551111111", "hello", "notif-1");

    expect(result.statusCode).toBe(200);
    expect(result.body.ok).toBe(true);
    await provider.send(clinicA, "+905551111111", "hello", "notif-1");
    expect(sockets[0].sendMessage).toHaveBeenCalledTimes(1);
  });

  it("keeps reporting a transient error when the message really was not sent", async () => {
    const { provider, sockets } = await readyProvider();
    sockets[0].sendMessage.mockRejectedValue(new Error("socket timeout"));

    const result = await provider.send(clinicA, "+905551111111", "hello", "notif-2");

    expect(result.statusCode).toBe(502);
    expect(result.body.errorCode).toBe("WORKER_TRANSIENT");
  });
});

describe("startup and event handlers", () => {
  it("starts the other clinics when one clinic's session data is unreadable", async () => {
    const sessionStore = storeWith({
      listClinicIds: vi.fn(async () => [clinicA, clinicB]),
      useAuthState: vi.fn(async (clinicId) => {
        if (clinicId === clinicA) throw new SyntaxError("Unexpected end of JSON input");
        return authState();
      })
    });
    const { provider, sockets } = createProvider({ sessionStore });

    await expect(provider.restoreSessions()).resolves.toBeUndefined();

    expect(sockets).toHaveLength(1);
    expect(provider.status(clinicA).status).toBe("auth_failed");
    expect(provider.status(clinicA).lastError).toContain("yeniden eşleştirin");
    expect(provider.status(clinicB).status).toBe("connecting");
  });

  it("contains failures inside socket event handlers instead of leaking unhandled rejections", async () => {
    const sessionStore = storeWith({
      useAuthState: vi.fn(async () => authState({ saveCreds: vi.fn().mockRejectedValue(new Error("cannot write creds")) }))
    });
    const { provider, sockets } = createProvider({ sessionStore });
    await provider.connect(clinicA);

    sockets[0].ev.emit("creds.update", {});
    await flush();
    await flush();

    expect(provider.logger.error).toHaveBeenCalledWith(
      expect.objectContaining({ event: "creds.update" }),
      "WhatsApp event handler failed"
    );
  });
});

describe("inbound messages", () => {
  it("truncates texts the gateway would reject and sets a request timeout", async () => {
    const httpClient = { post: vi.fn().mockResolvedValue({}) };
    const { provider, sockets } = createProvider({ httpClient });
    await provider.connect(clinicA);

    sockets[0].emitMessages({
      type: "notify",
      messages: [{
        key: { id: "long-1", fromMe: false, remoteJid: "905551111111@s.whatsapp.net" },
        message: { conversation: "x".repeat(5000) },
        messageTimestamp: 1
      }]
    });
    await flush();

    expect(httpClient.post.mock.calls[0][1].message).toHaveLength(4096);
    expect(httpClient.post.mock.calls[0][2].timeout).toBeGreaterThan(0);
  });

  it("quarantines a permanently rejected message and keeps delivering the ones behind it", async () => {
    const queue = [
      { file: "bad.json", payload: { provider_message_id: "bad" } },
      { file: "good.json", payload: { provider_message_id: "good" } }
    ];
    const quarantined = [];
    const removed = [];
    const sessionStore = storeWith({
      listClinicIds: vi.fn(async () => []),
      listPendingInbound: vi.fn(async () => queue),
      quarantinePendingInbound: vi.fn(async (_clinic, file) => quarantined.push(file)),
      removePendingInbound: vi.fn(async (_clinic, file) => removed.push(file))
    });
    const httpClient = {
      post: vi.fn(async (_url, payload) => {
        if (payload.provider_message_id === "bad") throw Object.assign(new Error("422"), { response: { status: 422 } });
        return {};
      })
    };
    const { provider } = createProvider({ sessionStore, httpClient });

    await provider.retryPendingInbound(clinicA);

    expect(quarantined).toEqual(["bad.json"]);
    expect(removed).toEqual(["good.json"]);
  });

  it("keeps the queue untouched while the gateway rejects the worker token", async () => {
    const queue = [
      { file: "a.json", payload: { provider_message_id: "a" } },
      { file: "b.json", payload: { provider_message_id: "b" } }
    ];
    const sessionStore = storeWith({
      listClinicIds: vi.fn(async () => []),
      listPendingInbound: vi.fn(async () => queue),
      quarantinePendingInbound: vi.fn(),
      removePendingInbound: vi.fn()
    });
    const httpClient = { post: vi.fn().mockRejectedValue(Object.assign(new Error("401"), { response: { status: 401 } })) };
    const { provider } = createProvider({ sessionStore, httpClient });

    await provider.retryPendingInbound(clinicA);

    expect(httpClient.post).toHaveBeenCalledTimes(1);
    expect(sessionStore.quarantinePendingInbound).not.toHaveBeenCalled();
    expect(sessionStore.removePendingInbound).not.toHaveBeenCalled();
  });
});

describe("FileSessionStore resilience", () => {
  it("quarantines queue files and ignores unreadable ones", async () => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "wa-store-"));
    try {
      const store = new FileSessionStore({ sessionRoot: root, sessionEncryptionKey: "test-key-test-key-test-key-123456" });
      await store.savePendingInbound(clinicA, { provider_message_id: "ok-1" });
      await store.savePendingInbound(clinicA, { provider_message_id: "bad-1" });
      const dir = path.join(root, "pending-inbound", clinicA);
      await fs.writeFile(path.join(dir, "corrupt.json"), "{not json");

      const listed = await store.listPendingInbound(clinicA);
      expect(listed.map((item) => item.payload.provider_message_id).sort()).toEqual(["bad-1", "ok-1"]);

      await store.quarantinePendingInbound(clinicA, "bad-1.json");
      const after = await store.listPendingInbound(clinicA);
      expect(after.map((item) => item.payload.provider_message_id)).toEqual(["ok-1"]);
      await expect(fs.stat(path.join(dir, "rejected", "bad-1.json"))).resolves.toBeTruthy();
    } finally {
      await fs.rm(root, { recursive: true, force: true });
    }
  });
});

describe("inbound queue safety", () => {
  it("keeps messages queued when the gateway route is missing instead of quarantining them", async () => {
    const queue = [{ file: "a.json", payload: { provider_message_id: "a" } }];
    const sessionStore = storeWith({
      listClinicIds: vi.fn(async () => []),
      listPendingInbound: vi.fn(async () => queue),
      quarantinePendingInbound: vi.fn(),
      removePendingInbound: vi.fn()
    });
    const httpClient = { post: vi.fn().mockRejectedValue(Object.assign(new Error("404"), { response: { status: 404 } })) };
    const { provider } = createProvider({ sessionStore, httpClient });

    await provider.retryPendingInbound(clinicA);

    expect(sessionStore.quarantinePendingInbound).not.toHaveBeenCalled();
    expect(sessionStore.removePendingInbound).not.toHaveBeenCalled();
  });

  it("does not forward a message that only contains blanks", async () => {
    const httpClient = { post: vi.fn().mockResolvedValue({}) };
    const { provider, sockets } = createProvider({ httpClient });
    await provider.connect(clinicA);

    sockets[0].emitMessages({
      type: "notify",
      messages: [{
        key: { id: "blank-1", fromMe: false, remoteJid: "905551111111@s.whatsapp.net" },
        message: { conversation: "  \n " },
        messageTimestamp: 1
      }]
    });
    await flush();

    expect(httpClient.post).not.toHaveBeenCalled();
  });

  it("never cuts an emoji in half when shortening a long message", async () => {
    const { truncateByCodePoints } = await import("../src/baileysProvider.js");
    const text = "a".repeat(4095) + "😀" + "tail";

    const shortened = truncateByCodePoints(text, 4096);

    expect(Array.from(shortened)).toHaveLength(4096);
    expect(shortened.endsWith("😀")).toBe(true);
    expect(shortened).not.toMatch(/[\uD800-\uDBFF]$/);
    expect(truncateByCodePoints("kısa", 4096)).toBe("kısa");
  });
});
