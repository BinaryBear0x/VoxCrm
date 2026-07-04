import { normalizePhoneNumber } from "./security.js";

export function buildDirectJid(phoneNumber) {
  return `${normalizePhoneNumber(phoneNumber)}@s.whatsapp.net`;
}

export async function resolveRecipientJid(socket, phoneNumber) {
  const digits = normalizePhoneNumber(phoneNumber);
  const result = await socket.onWhatsApp(digits);
  const match = Array.isArray(result) ? result.find((item) => item?.exists && item?.jid) : null;
  return match?.jid || null;
}

export function shouldIgnoreInbound(message) {
  const remoteJid = message?.key?.remoteJid || "";
  return Boolean(
    message?.key?.fromMe ||
    remoteJid.endsWith("@g.us") ||
    remoteJid === "status@broadcast" ||
    remoteJid.endsWith("@broadcast") ||
    remoteJid.endsWith("@newsletter")
  );
}

export function inboundFromPhone(message) {
  return message?.key?.remoteJid || "";
}

export function extractInboundText(message) {
  const content = unwrapMessage(message?.message);
  if (!content) return "";

  if (content.conversation) return content.conversation;
  if (content.extendedTextMessage?.text) return content.extendedTextMessage.text;
  if (content.imageMessage) return "[media:image]";
  if (content.videoMessage) return "[media:video]";
  if (content.audioMessage) return "[media:audio]";
  if (content.documentMessage) return "[media:document]";
  if (content.stickerMessage) return "[media:sticker]";
  if (content.locationMessage) return "[location]";
  return "[unsupported-message]";
}

export function messageTimestampToIso(message) {
  const raw = message?.messageTimestamp;
  const seconds = typeof raw === "number"
    ? raw
    : typeof raw?.toNumber === "function"
      ? raw.toNumber()
      : Date.now() / 1000;
  return new Date(seconds * 1000).toISOString();
}

function unwrapMessage(content) {
  if (!content) return null;
  if (content.ephemeralMessage?.message) return unwrapMessage(content.ephemeralMessage.message);
  if (content.viewOnceMessage?.message) return unwrapMessage(content.viewOnceMessage.message);
  if (content.viewOnceMessageV2?.message) return unwrapMessage(content.viewOnceMessageV2.message);
  if (content.documentWithCaptionMessage?.message) return unwrapMessage(content.documentWithCaptionMessage.message);
  return content;
}
