# syntax=docker/dockerfile:1.6
# Multi-stage build for the Angular SPA. API URL is injected via build arg so
# the same image can be promoted across envs.

FROM node:20-bookworm-slim AS build
WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm ci

COPY . .

# Inject the API origin (e.g. https://liga305-api.yourdomain.com) before build.
# The SPA's runtime fetches hit this URL directly.
ARG API_BASE_URL=http://localhost:5080
RUN sed -i "s|__API_BASE_URL__|${API_BASE_URL}|g" src/environments/environment.prod.ts

RUN npx ng build --configuration=production

FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/dist/liga305-web/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
