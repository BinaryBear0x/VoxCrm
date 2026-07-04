import crypto from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";

const CLINIC_ID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const DEV_ONLY_WORKER_TOKEN = "dev-only-worker-token-change-me";

export function requireClinicId(value) {
  if (!CLINIC_ID_PATTERN.test(String(value || ""))) {
    const error = new Error("Invalid clinic id.");
    error.status = 400;
    throw error;
  }
  return String(value).toLowerCase();
}

export function normalizePhoneNumber(value) {
  const digits = String(value || "").replace(/\D/g, "");
  if (digits.length < 10 || digits.length > 15) {
    const error = new Error("Invalid phone number.");
    error.errorCode = "INVALID_PHONE";
    throw error;
  }
  return digits;
}

export function resolveClinicSessionPath(sessionRoot, clinicId) {
  const root = path.resolve(sessionRoot);
  const clinicPath = path.resolve(root, "baileys", clinicId);
  const relative = path.relative(root, clinicPath);
  if (relative.startsWith("..") || path.isAbsolute(relative)) {
    const error = new Error("Invalid session path.");
    error.status = 400;
    throw error;
  }
  return clinicPath;
}

export async function ensurePrivateDir(directory) {
  await fs.mkdir(directory, { recursive: true, mode: 0o700 });
  await fs.chmod(directory, 0o700).catch(() => undefined);
}

export async function writePrivateFile(filePath, data) {
  await ensurePrivateDir(path.dirname(filePath));
  const tempPath = `${filePath}.${process.pid}.${Date.now()}.${crypto.randomUUID()}.tmp`;
  await fs.writeFile(tempPath, data, { mode: 0o600 });
  await fs.chmod(tempPath, 0o600).catch(() => undefined);
  await ensurePrivateDir(path.dirname(filePath));
  await fs.rename(tempPath, filePath);
}

export function isDevWorkerToken(value) {
  return value === DEV_ONLY_WORKER_TOKEN;
}

export function timingSafeTokenEquals(value, expected) {
  const actualBuffer = Buffer.from(String(value || ""));
  const expectedBuffer = Buffer.from(String(expected || ""));
  if (actualBuffer.length !== expectedBuffer.length || expectedBuffer.length === 0) {
    return false;
  }

  return crypto.timingSafeEqual(actualBuffer, expectedBuffer);
}

export function maskPhone(value) {
  const digits = String(value || "").replace(/\D/g, "");
  if (digits.length <= 4) return "****";
  return `${"*".repeat(Math.max(0, digits.length - 4))}${digits.slice(-4)}`;
}

export function createCipher(sessionEncryptionKey) {
  if (!sessionEncryptionKey) {
    return {
      enabled: false,
      encode: (plainText) => plainText,
      decode: (payload) => payload
    };
  }

  const key = deriveKey(sessionEncryptionKey);
  return {
    enabled: true,
    encode: (plainText) => {
      const iv = crypto.randomBytes(12);
      const cipher = crypto.createCipheriv("aes-256-gcm", key, iv);
      const encrypted = Buffer.concat([cipher.update(plainText, "utf8"), cipher.final()]);
      const tag = cipher.getAuthTag();
      return JSON.stringify({
        v: 1,
        alg: "aes-256-gcm",
        iv: iv.toString("base64"),
        tag: tag.toString("base64"),
        data: encrypted.toString("base64")
      });
    },
    decode: (payload) => {
      const parsed = JSON.parse(payload);
      if (parsed?.v !== 1 || parsed?.alg !== "aes-256-gcm") {
        throw new Error("Unsupported encrypted session payload.");
      }
      const decipher = crypto.createDecipheriv(
        "aes-256-gcm",
        key,
        Buffer.from(parsed.iv, "base64")
      );
      decipher.setAuthTag(Buffer.from(parsed.tag, "base64"));
      return Buffer.concat([
        decipher.update(Buffer.from(parsed.data, "base64")),
        decipher.final()
      ]).toString("utf8");
    }
  };
}

function deriveKey(value) {
  const raw = Buffer.from(value, "base64");
  if (raw.length === 32) return raw;
  return crypto.createHash("sha256").update(value).digest();
}
