FROM node:22-bookworm-slim@sha256:6c74791e557ce11fc957704f6d4fe134a7bc8d6f5ca4403205b2966bd488f6b3

ENV NODE_ENV=production
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd --system voxcrm && useradd --system --gid voxcrm --home-dir /app voxcrm

COPY package*.json ./
RUN npm ci --omit=dev
COPY src ./src

RUN chown -R voxcrm:voxcrm /app \
    && rm -rf /usr/local/lib/node_modules/npm /usr/local/bin/npm /usr/local/bin/npx

USER voxcrm

EXPOSE 8090
CMD ["node", "src/index.js"]
