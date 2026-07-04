import pino from "pino";
import { createApp } from "./app.js";
import { BaileysProvider } from "./baileysProvider.js";
import { loadConfig, validateRuntime } from "./config.js";
import { FileSessionStore } from "./sessionStore.js";

const config = loadConfig();
validateRuntime(config);

const logger = pino({ level: config.logLevel });
const sessionStore = new FileSessionStore({
  sessionRoot: config.sessionRoot,
  sessionEncryptionKey: config.sessionEncryptionKey
});
const provider = new BaileysProvider({
  sessionStore,
  gatewayInternalUrl: config.gatewayInternalUrl,
  workerInternalToken: config.workerInternalToken,
  logger
});
const app = createApp({ provider, workerInternalToken: config.workerInternalToken });

app.listen(config.port, () => {
  logger.info({ port: config.port, provider: "baileys" }, "wa-worker listening");
});
