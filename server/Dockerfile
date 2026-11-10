# syntax=docker/dockerfile:1
# Собирается из корня репозитория: docker build -f server/Dockerfile .
# Что попадает в контекст — см. server/Dockerfile.dockerignore.

FROM node:24-alpine AS builder
RUN apk add --no-cache git
WORKDIR /build
COPY server/modules/package.json server/modules/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY server/modules/tsconfig.json ./
COPY server/modules/src ./src
RUN npm run build

FROM heroiclabs/nakama:3.42.0
COPY --from=builder /build/build/index.js /nakama/data/modules/build/index.js
# Данные рыбы и снастей: на сервере читаются через nk.fileRead("data/fish.json")
COPY shared/data/ /nakama/data/modules/data/
COPY server/local.yml /nakama/data/local.yml
