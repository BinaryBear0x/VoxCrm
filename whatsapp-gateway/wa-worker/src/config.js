export function loadConfig(env = process.env) {
  return {
    port: Number(env.PORT || 8090),
    sessionRoot: env.SESSION_ROOT || "./sessions",
    gatewayInternalUrl: env.GATEWAY_INTERNAL_URL || "http://127.0.0.1:8088",
    workerInternalToken: env.WORKER_INTERNAL_TOKEN || "dev-only-worker-token-change-me",
    nodeEnv: env.NODE_ENV || "development",
    sessionEncryptionKey: env.WORKER_SESSION_ENCRYPTION_KEY || "",
    logLevel: env.LOG_LEVEL || "info"
  };
}

export function validateRuntime(config) {
  if (config.nodeEnv === "production") {
    if (config.workerInternalToken === "dev-only-worker-token-change-me") {
      throw new Error("WORKER_INTERNAL_TOKEN must be changed in production.");
    }
    if (!config.sessionEncryptionKey) {
      throw new Error("WORKER_SESSION_ENCRYPTION_KEY must be configured in production.");
    }
  }
}
