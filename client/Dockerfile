# The web client as a deployable image: the static SvelteKit build, served by Caddy, which also
# proxies the API (/1/..., including the live websocket) to the api container. Page and API stay
# same-origin, exactly as `vite dev` arranges it, and Caddy is where HTTPS comes from (VRCX_SITE).
#
# Build context is the repository root: the client's protobuf types are generated from
# server/Common/Protos, the same .proto the server and the desktop app compile.
#   docker build -f client/Dockerfile .

FROM node:26.13.0-alpine3.24 AS build
WORKDIR /repo/client
RUN npm install -g pnpm@11.15.1

COPY client/package.json client/pnpm-lock.yaml client/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY server/Common/Protos /repo/server/Common/Protos
COPY client/ ./
RUN pnpm run build

FROM caddy:2.11.9-alpine AS web
COPY client/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /repo/client/build /srv
