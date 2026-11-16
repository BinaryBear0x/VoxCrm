import { describe, expect, it } from "vitest";
import { inboundFromPhone } from "../src/messageUtils.js";

describe("inboundFromPhone", () => {
  it("returns the phone-number address of a normal chat", () => {
    expect(inboundFromPhone({ key: { remoteJid: "905551112233@s.whatsapp.net" } })).toBe("905551112233@s.whatsapp.net");
  });

  it("prefers the real phone-number address when the chat is identified by a LID", () => {
    const message = { key: { remoteJid: "123456789012345@lid", remoteJidAlt: "905551112233@s.whatsapp.net" } };
    expect(inboundFromPhone(message)).toBe("905551112233@s.whatsapp.net");
  });

  it("keeps the LID when no phone-number address is known", () => {
    expect(inboundFromPhone({ key: { remoteJid: "123456789012345@lid" } })).toBe("123456789012345@lid");
  });

  it("does not trust an alternate address that is not a phone number", () => {
    const message = { key: { remoteJid: "123456789012345@lid", remoteJidAlt: "other@lid" } };
    expect(inboundFromPhone(message)).toBe("123456789012345@lid");
  });
});

describe("extractInboundText", () => {
  it("ignores protocol traffic that is not a customer message", async () => {
    const { extractInboundText } = await import("../src/messageUtils.js");
    expect(extractInboundText({ message: { reactionMessage: { text: "👍" } } })).toBe("");
    expect(extractInboundText({ message: { protocolMessage: { type: 0 } } })).toBe("");
    expect(extractInboundText({ message: { conversation: "Merhaba" } })).toBe("Merhaba");
    expect(extractInboundText({ message: { imageMessage: {} } })).toBe("[media:image]");
  });
});
