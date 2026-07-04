import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { FileSessionStore } from "../src/sessionStore.js";
import {
  normalizePhoneNumber,
  requireClinicId,
  resolveClinicSessionPath
} from "../src/security.js";

const clinicId = "73ccade0-16f3-4d2d-b55d-fe152ae4d05b";

describe("worker security helpers", () => {
  it("validates clinic ids and phone numbers", () => {
    expect(requireClinicId(clinicId.toUpperCase())).toBe(clinicId);
    expect(() => requireClinicId("../../bad")).toThrow("Invalid clinic id");
    expect(normalizePhoneNumber("+90 (555) 111-11-11")).toBe("905551111111");
    expect(() => normalizePhoneNumber("123")).toThrow("Invalid phone number");
  });

  it("keeps clinic session paths under SESSION_ROOT", () => {
    const root = "/tmp/voxcrm-worker-sessions";
    expect(resolveClinicSessionPath(root, clinicId)).toBe(path.resolve(root, "baileys", clinicId));
  });

  it("stores auth state in clinic scoped private files", async () => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "voxcrm-worker-store-"));
    const store = new FileSessionStore({ sessionRoot: root, sessionEncryptionKey: "" });

    const auth = await store.useAuthState(clinicId);
    auth.state.creds.me = { id: "905550000000@s.whatsapp.net" };
    await auth.saveCreds();

    const authPath = path.join(root, "baileys", clinicId, "auth.json");
    const stat = await fs.stat(authPath);
    expect((stat.mode & 0o777).toString(8)).toBe("600");

    const restored = await store.useAuthState(clinicId);
    expect(restored.state.creds.me.id).toBe("905550000000@s.whatsapp.net");
  });

  it("encrypts auth state when an encryption key is configured", async () => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "voxcrm-worker-store-"));
    const store = new FileSessionStore({
      sessionRoot: root,
      sessionEncryptionKey: "test-encryption-key"
    });

    const auth = await store.useAuthState(clinicId);
    auth.state.creds.me = { id: "905550000000@s.whatsapp.net" };
    await auth.saveCreds();

    const raw = await fs.readFile(path.join(root, "baileys", clinicId, "auth.json"), "utf8");
    expect(raw).toContain('"alg":"aes-256-gcm"');
    expect(raw).not.toContain("905550000000");
    expect(store.encryptionStatus()).toBe("enabled");
  });
});
