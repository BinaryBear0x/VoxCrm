import fs from "node:fs/promises";
import path from "node:path";
import { BufferJSON, initAuthCreds, proto } from "baileys";
import {
  createCipher,
  ensurePrivateDir,
  resolveClinicSessionPath,
  writePrivateFile
} from "./security.js";

const AUTH_FILE = "auth.json";

export class FileSessionStore {
  constructor({ sessionRoot, sessionEncryptionKey = "" }) {
    this.sessionRoot = sessionRoot;
    this.cipher = createCipher(sessionEncryptionKey);
  }

  encryptionStatus() {
    return this.cipher.enabled ? "enabled" : "disabled";
  }

  async useAuthState(clinicId) {
    const sessionPath = resolveClinicSessionPath(this.sessionRoot, clinicId);
    await ensurePrivateDir(sessionPath);
    await ensurePrivateDir(path.join(sessionPath, "keys"));
    await ensurePrivateDir(path.join(sessionPath, "messages"));
    await ensurePrivateDir(path.join(sessionPath, "receipts"));

    const creds = (await this.#readJson(path.join(sessionPath, AUTH_FILE))) || initAuthCreds();

    return {
      state: {
        creds,
        keys: {
          get: async (type, ids) => {
            const result = {};
            await Promise.all(ids.map(async (id) => {
              let value = await this.#readJson(this.#keyPath(sessionPath, type, id));
              if (type === "app-state-sync-key" && value) {
                value = proto.Message.AppStateSyncKeyData.fromObject(value);
              }
              result[id] = value;
            }));
            return result;
          },
          set: async (data) => {
            const tasks = [];
            for (const [type, values] of Object.entries(data)) {
              for (const [id, value] of Object.entries(values)) {
                const filePath = this.#keyPath(sessionPath, type, id);
                tasks.push(value ? this.#writeJson(filePath, value) : this.#deleteFile(filePath));
              }
            }
            await Promise.all(tasks);
          }
        }
      },
      saveCreds: async () => this.#writeJson(path.join(sessionPath, AUTH_FILE), creds),
      getMessage: async (key) => this.getMessage(clinicId, key),
      saveMessage: async (key, message) => this.saveMessage(clinicId, key, message)
    };
  }

  async getMessage(clinicId, key) {
    const sessionPath = resolveClinicSessionPath(this.sessionRoot, clinicId);
    return await this.#readJson(this.#messagePath(sessionPath, key));
  }

  async saveMessage(clinicId, key, message) {
    if (!key || !message) return;
    const sessionPath = resolveClinicSessionPath(this.sessionRoot, clinicId);
    await this.#writeJson(this.#messagePath(sessionPath, key), message);
  }

  async getSentNotification(clinicId, notificationId) {
    const sessionPath = resolveClinicSessionPath(this.sessionRoot, clinicId);
    return await this.#readJson(path.join(sessionPath, "receipts", `${safeName(notificationId)}.json`));
  }

  async saveSentNotification(clinicId, notificationId, messageId) {
    const sessionPath = resolveClinicSessionPath(this.sessionRoot, clinicId);
    await ensurePrivateDir(path.join(sessionPath, "receipts"));
    await this.#writeJson(
      path.join(sessionPath, "receipts", `${safeName(notificationId)}.json`),
      { messageId, sentAt: new Date().toISOString() }
    );
  }

  async savePendingInbound(clinicId, payload) {
    const directory = this.#pendingInboundPath(clinicId);
    await ensurePrivateDir(directory);
    await this.#writeJson(
      path.join(directory, `${safeName(payload.provider_message_id)}.json`),
      payload
    );
  }

  async listPendingInbound(clinicId) {
    const directory = this.#pendingInboundPath(clinicId);
    try {
      const files = await fs.readdir(directory);
      return (await Promise.all(files.filter((file) => file.endsWith(".json")).map(async (file) => ({
        file,
        payload: await this.#readJson(path.join(directory, file))
      })))).filter((item) => item.payload);
    } catch (error) {
      if (error?.code === "ENOENT") return [];
      throw error;
    }
  }

  async removePendingInbound(clinicId, file) {
    const directory = this.#pendingInboundPath(clinicId);
    await fs.rm(path.join(directory, safeName(file)), { force: true });
  }

  async listPendingClinicIds() {
    const root = path.resolve(this.sessionRoot, "pending-inbound");
    try {
      const entries = await fs.readdir(root, { withFileTypes: true });
      return entries
        .filter((entry) => entry.isDirectory())
        .map((entry) => entry.name)
        .filter((clinicId) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(clinicId));
    } catch (error) {
      if (error?.code === "ENOENT") return [];
      throw error;
    }
  }

  async removeClinic(clinicId) {
    const sessionPath = resolveClinicSessionPath(this.sessionRoot, clinicId);
    await fs.rm(sessionPath, { recursive: true, force: true });
  }

  async listClinicIds() {
    const root = path.resolve(this.sessionRoot, "baileys");
    try {
      const entries = await fs.readdir(root, { withFileTypes: true });
      return entries
        .filter((entry) => entry.isDirectory())
        .map((entry) => entry.name)
        .filter((clinicId) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(clinicId));
    } catch (error) {
      if (error?.code === "ENOENT") return [];
      throw error;
    }
  }

  #keyPath(sessionPath, type, id) {
    return path.join(sessionPath, "keys", `${safeName(type)}-${safeName(id)}.json`);
  }

  #messagePath(sessionPath, key) {
    const id = key?.id || "unknown";
    const remoteJid = key?.remoteJid || "unknown";
    return path.join(sessionPath, "messages", `${safeName(remoteJid)}-${safeName(id)}.json`);
  }

  #pendingInboundPath(clinicId) {
    const safeClinicId = resolveClinicSessionPath(this.sessionRoot, clinicId).split(path.sep).at(-1);
    return path.resolve(this.sessionRoot, "pending-inbound", safeClinicId);
  }

  async #readJson(filePath) {
    try {
      const payload = await fs.readFile(filePath, "utf8");
      const plainText = this.cipher.decode(payload);
      return JSON.parse(plainText, BufferJSON.reviver);
    } catch (error) {
      if (error?.code === "ENOENT") return null;
      throw error;
    }
  }

  async #writeJson(filePath, value) {
    const plainText = JSON.stringify(value, BufferJSON.replacer);
    await writePrivateFile(filePath, this.cipher.encode(plainText));
  }

  async #deleteFile(filePath) {
    await fs.rm(filePath, { force: true });
  }
}

function safeName(value) {
  return String(value).replace(/[^a-zA-Z0-9_.@-]/g, "_");
}
