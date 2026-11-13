# syntax=docker/dockerfile:1
# Build from web/:
#   docker build --build-arg ALLOWED_HOSTS=stats.example.com -t custom-golf-stats-web web
# ALLOWED_HOSTS is baked in at build time (see README.md). API_BASE_URL and SESSION_SECRET are runtime environment only.

ARG NODE_VERSION=24

# Production dependencies only, for the runtime image.
FROM node:${NODE_VERSION}-bookworm-slim AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev --ignore-scripts --no-audit --no-fund

FROM node:${NODE_VERSION}-bookworm-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
ARG ALLOWED_HOSTS=""
ENV ALLOWED_HOSTS=${ALLOWED_HOSTS} \
    ASTRO_TELEMETRY_DISABLED=1
RUN npm run build

FROM node:${NODE_VERSION}-bookworm-slim AS runtime
ENV NODE_ENV=production \
    HOST=0.0.0.0 \
    PORT=4321
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
COPY package.json ./
# The node image's non-root user; the files above stay root-owned, so it can't modify them.
USER node
EXPOSE 4321
# /sign-in renders without calling the API, so this checks the server itself.
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD ["node", "-e", "fetch('http://127.0.0.1:'+(process.env.PORT||4321)+'/sign-in').then(r=>process.exit(r.ok?0:1),()=>process.exit(1))"]
CMD ["node", "./dist/server/entry.mjs"]
