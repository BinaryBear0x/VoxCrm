import { describe, expect, it } from "vitest";
import { normalizePhoneNumber } from "../src/security.js";

// The CRM (WhatsAppRecipientGuard.Digits) approves a recipient; the worker must address
// exactly that number. Keep these cases identical to PhoneDigitsTests on the .NET side.
describe("normalizePhoneNumber matches the CRM's recipient rules", () => {
  it.each([
    ["+90 555 111 22 33", "905551112233"],
    ["05551112233", "905551112233"],
    ["5551112233", "905551112233"],
    ["905551112233", "905551112233"],
    ["0090 555 111 22 33", "905551112233"],
    ["+44 20 7946 0018", "442079460018"],
    // Turkish landlines are 10 digits that do not start with 5; the CRM treats them as Turkish too.
    ["212 555 12 34", "902125551234"]
  ])("%s -> %s", (input, expected) => {
    expect(normalizePhoneNumber(input)).toBe(expected);
  });

  it("still rejects numbers that are too short or too long", () => {
    expect(() => normalizePhoneNumber("123")).toThrow("Invalid phone number");
    expect(() => normalizePhoneNumber("1234567890123456")).toThrow("Invalid phone number");
  });
});
