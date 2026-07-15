import express from "express";
import { requireClinicId, timingSafeTokenEquals } from "./security.js";

export function createApp({ provider, workerInternalToken }) {
  const app = express();
  app.use(express.json({ limit: "1mb" }));
  app.use((req, res, next) => {
    if (!timingSafeTokenEquals(req.get("x-internal-token"), workerInternalToken)) {
      return res.status(401).json({ ok: false, errorCode: "UNAUTHORIZED", error: "Unauthorized." });
    }

    return next();
  });

  app.get("/health", (_req, res) => {
    res.json(provider.health());
  });

  app.post("/clinics/:clinicId/connect", async (req, res, next) => {
    try {
      const clinicId = requireClinicId(req.params.clinicId);
      res.json(await provider.connect(clinicId));
    } catch (error) {
      next(error);
    }
  });

  app.post("/clinics/:clinicId/disconnect", async (req, res, next) => {
    try {
      const clinicId = requireClinicId(req.params.clinicId);
      res.json(await provider.disconnect(clinicId));
    } catch (error) {
      next(error);
    }
  });

  app.get("/clinics/:clinicId/status", (req, res, next) => {
    try {
      const clinicId = requireClinicId(req.params.clinicId);
      res.json(provider.status(clinicId));
    } catch (error) {
      next(error);
    }
  });

  app.get("/clinics/:clinicId/qr", (req, res, next) => {
    try {
      const clinicId = requireClinicId(req.params.clinicId);
      res.json(provider.qr(clinicId));
    } catch (error) {
      next(error);
    }
  });

  app.post("/clinics/:clinicId/send", async (req, res, next) => {
    try {
      const clinicId = requireClinicId(req.params.clinicId);
      const phoneNumber = requiredString(req.body.phoneNumber, "phoneNumber", 32);
      const message = requiredString(req.body.message, "message", 4096);
      const notificationId = requiredString(req.body.notificationId, "notificationId", 128);
      const result = await provider.send(
        clinicId,
        phoneNumber,
        message,
        notificationId
      );
      res.status(result.statusCode || 200).json(result.body || result);
    } catch (error) {
      next(error);
    }
  });

  app.use((error, _req, res, _next) => {
    res.status(error.status || 400).json({
      ok: false,
      errorCode: error.errorCode || "BAD_REQUEST",
      error: error.message
    });
  });

  return app;
}

function requiredString(value, field, maxLength) {
  if (typeof value !== "string" || !value.trim() || value.length > maxLength) {
    const error = new Error(`${field} is required and must be at most ${maxLength} characters.`);
    error.status = 400;
    error.errorCode = "INVALID_PAYLOAD";
    throw error;
  }
  return value.trim();
}
