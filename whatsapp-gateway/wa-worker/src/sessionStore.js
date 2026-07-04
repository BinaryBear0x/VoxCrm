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

  async removeClinic(clinicId) {
    const sessionPath = resolveClinicSessionPath(this.sessionRoot, clinicId);
    await fs.rm(sessionPath, { recursive: true, force: true });
  }

  #keyPath(sessionPath, type, id) {
    return path.join(sessionPath, "keys", `${safeName(type)}-${safeName(id)}.json`);
  }

  #messagePath(sessionPath, key) {
    const id = key?.id || "unknown";
    const remoteJid = key?.remoteJid || "unknown";
    return path.join(sessionPath, "messages", `${safeName(remoteJid)}-${safeName(id)}.json`);
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
