# syntax=docker/dockerfile:1

# ---------- build ----------
FROM node:22-alpine AS build
WORKDIR /app

# Instala dependências (usa lockfile quando existir)
COPY package.json package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi

COPY . .

ARG VITE_API_BASE_URL=/api
ARG VITE_USE_MOCKS=false
# Baixa o GeoJSON de países caso ainda não esteja presente (exige rede no build).
ARG FETCH_GEO=true
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_USE_MOCKS=$VITE_USE_MOCKS

RUN if [ "$FETCH_GEO" = "true" ] && [ ! -f public/geo/countries-110m.geojson ]; then \
      npm run geo:fetch || echo "AVISO: GeoJSON não baixado; o mapa exibirá estado vazio."; \
    fi
RUN npm run build

# ---------- runtime ----------
FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
