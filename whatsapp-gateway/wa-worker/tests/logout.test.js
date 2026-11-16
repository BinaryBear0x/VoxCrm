import { EventEmitter } from "node:events";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { BaileysProvider } from "../src/baileysProvider.js";
import { FileSessionStore } from "../src/sessionStore.js";

const clinic = "73ccade0-16f3-4d2d-b55d-fe152ae4d05b";
const LOGGED_OUT = { error: { output: { statusCode: 401 }, message: "Logged Out" } };

function socketMock() {
  const ev = new EventEmitter();
  return {
    ev,
    user: null,
    logout: vi.fn().mockResolvedValue(undefined),
    end: vi.fn().mockResolvedValue(undefined),
    emitConnection: (payload) => ev.emit("connection.update", payload)
  };
}

function providerWith(sessionStore) {
  const sockets = [];
  const provider = new BaileysProvider({
    sessionStore,
    gatewayInternalUrl: "http://gateway-api:8088",
    workerInternalToken: "t",
    logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn() },
    httpClient: { post: vi.fn().mockResolvedValue({}) },
    makeSocket: () => { const s = socketMock(); sockets.push(s); return s; }
  });
  return { provider, sockets };
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe("linking again after the phone logged the device out", () => {
  it("drops the dead credentials so the next connect starts a fresh link", async () => {
    const sessionStore = {
      encryptionStatus: () => "disabled",
      useAuthState: vi.fn(async () => ({ state: { creds: {}, keys: { get: vi.fn(), set: vi.fn() } }, saveCreds: vi.fn(), getMessage: vi.fn(), saveMessage: vi.fn() })),
      resetAuthState: vi.fn().mockResolvedValue(undefined),
      removeClinic: vi.fn()
    };
    const { provider, sockets } = providerWith(sessionStore);
    await provider.connect(clinic);
    sockets[0].emitConnection({ connection: "open" });

    sockets[0].emitConnection({ connection: "close", lastDisconnect: LOGGED_OUT });
    await flush();

    expect(provider.status(clinic).status).toBe("auth_failed");
    expect(sessionStore.resetAuthState).toHaveBeenCalledWith(clinic);
    // The clinic's data (receipts, queued inbound messages) is not wiped, only authentication.
    expect(sessionStore.removeClinic).not.toHaveBeenCalled();

    await provider.connect(clinic);
    expect(sockets).toHaveLength(2);
    expect(provider.status(clinic).status).toBe("connecting");
    expect(provider.status(clinic).lastError).toBeNull();
  });

  it("still discards stale credentials when connect is pressed after a restart left the session in auth_failed", async () => {
    const sessionStore = {
      encryptionStatus: () => "disabled",
      listClinicIds: vi.fn(async () => [clinic]),
      useAuthState: vi.fn(async () => ({ state: { creds: {}, keys: { get: vi.fn(), set: vi.fn() } }, saveCreds: vi.fn(), getMessage: vi.fn(), saveMessage: vi.fn() })),
      resetAuthState: vi.fn().mockResolvedValue(undefined),
      listPendingInbound: vi.fn(async () => []),
      removeClinic: vi.fn()
    };
    const { provider, sockets } = providerWith(sessionStore);
    await provider.restoreSessions();
    sockets[0].emitConnection({ connection: "close", lastDisconnect: LOGGED_OUT });
    await flush();
    sessionStore.resetAuthState.mockClear();

    await provider.connect(clinic);

    expect(sessionStore.resetAuthState).toHaveBeenCalledWith(clinic);
    expect(sockets).toHaveLength(2);
  });

  it("does not turn a failing cleanup into a crash", async () => {
    const sessionStore = {
      encryptionStatus: () => "disabled",
      useAuthState: vi.fn(async () => ({ state: { creds: {}, keys: { get: vi.fn(), set: vi.fn() } }, saveCreds: vi.fn(), getMessage: vi.fn(), saveMessage: vi.fn() })),
      resetAuthState: vi.fn().mockRejectedValue(new Error("read-only disk")),
      removeClinic: vi.fn()
    };
    const { provider, sockets } = providerWith(sessionStore);
    await provider.connect(clinic);

    sockets[0].emitConnection({ connection: "close", lastDisconnect: LOGGED_OUT });
    await flush();

    expect(provider.status(clinic).status).toBe("auth_failed");
    expect(provider.logger.error).toHaveBeenCalled();
  });
});

describe("FileSessionStore.resetAuthState", () => {
  it("removes credentials and keys but keeps receipts and the inbound queue", async () => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "wa-reset-"));
    try {
      const store = new FileSessionStore({ sessionRoot: root, sessionEncryptionKey: "test-key-test-key-test-key-123456" });
      const auth = await store.useAuthState(clinic);
      await auth.saveCreds();
      await auth.state.keys.set({ "pre-key": { 1: { private: Buffer.from("x"), public: Buffer.from("y") } } });
      await store.saveSentNotification(clinic, "notif-1", "wa-1");
      await store.savePendingInbound(clinic, { provider_message_id: "in-1" });
      const sessionDir = path.join(root, "baileys", clinic);
      await expect(fs.stat(path.join(sessionDir, "auth.json"))).resolves.toBeTruthy();

      await store.resetAuthState(clinic);

      await expect(fs.stat(path.join(sessionDir, "auth.json"))).rejects.toThrow();
      await expect(fs.stat(path.join(sessionDir, "keys"))).rejects.toThrow();
      expect((await store.getSentNotification(clinic, "notif-1")).messageId).toBe("wa-1");
      expect(await store.listPendingInbound(clinic)).toHaveLength(1);

      // A fresh start after the reset yields brand-new credentials instead of the old ones.
      const fresh = await store.useAuthState(clinic);
      expect(fresh.state.creds).toBeTruthy();
    } finally {
      await fs.rm(root, { recursive: true, force: true });
    }
  });
});
