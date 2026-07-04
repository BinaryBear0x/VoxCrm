import { describe, expect, it, vi } from "vitest";
import request from "supertest";
import { createApp } from "../src/app.js";

const clinicId = "73ccade0-16f3-4d2d-b55d-fe152ae4d05b";
const workerInternalToken = "test-worker-token";

describe("worker http contract", () => {
  it("rejects requests without the internal worker token", async () => {
    const provider = { health: vi.fn() };

    const response = await request(createApp({ provider, workerInternalToken })).get("/health");

    expect(response.status).toBe(401);
    expect(provider.health).not.toHaveBeenCalled();
  });

  it("returns Baileys health metadata", async () => {
    const provider = {
      health: () => ({
        status: "ok",
        service: "wa-worker",
        provider: "baileys",
        sessions: 0,
        readySessions: 0,
        qrSessions: 0,
        authFailedSessions: 0,
        sessionEncryption: "enabled"
      })
    };

    const response = await request(createApp({ provider, workerInternalToken }))
      .get("/health")
      .set("x-internal-token", workerInternalToken);

    expect(response.status).toBe(200);
    expect(response.body.provider).toBe("baileys");
    expect(response.body.sessionEncryption).toBe("enabled");
  });

  it("rejects invalid clinic ids before reaching provider", async () => {
    const provider = { status: vi.fn() };

    const response = await request(createApp({ provider, workerInternalToken }))
      .get("/clinics/../../bad/status")
      .set("x-internal-token", workerInternalToken);

    expect(response.status).toBe(404);
    expect(provider.status).not.toHaveBeenCalled();
  });

  it("routes send result status and body without changing gateway contract", async () => {
    const provider = {
      send: vi.fn().mockResolvedValue({
        statusCode: 409,
        body: { ok: false, errorCode: "SESSION_NOT_READY", error: "WhatsApp session is not ready." }
      })
    };

    const response = await request(createApp({ provider, workerInternalToken }))
      .post(`/clinics/${clinicId}/send`)
      .set("x-internal-token", workerInternalToken)
      .send({ phoneNumber: "+905551111111", message: "hello", notificationId: "n1" });

    expect(response.status).toBe(409);
    expect(response.body.errorCode).toBe("SESSION_NOT_READY");
    expect(provider.send).toHaveBeenCalledWith(clinicId, "+905551111111", "hello", "n1");
  });
});
