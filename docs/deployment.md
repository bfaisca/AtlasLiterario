# Publicação (Vercel + Render + Neon)

Arquitetura de produção, no mesmo desenho do FinanceGroup:

```
Navegador ──► Vercel (frontend estático + rewrite /api/*) ──► Render (API .NET em Docker) ──► Neon (PostgreSQL)
```

O navegador só fala com o domínio do Vercel; o `vercel.json` repassa `/api/*` e `/health` para o Render. Por ser a mesma origem, **não há CORS em jogo** (a configuração de CORS da API só importa se algum dia o frontend chamar a API direto).

> E-mail (redefinição de senha) **fica para depois**: a API usa `LoggingEmailSender`, que só escreve no log. Em produção, "esqueci a senha" não entrega nada. Quando for implementado, o Render gratuito bloqueia SMTP; usar um provedor por HTTPS (o FinanceGroup usa a Brevo).

## 0. Antes de tudo

1. Fazer backup do banco local: `.\scripts\backup-db.ps1`.
2. Mesclar a branch de trabalho na `main` (Vercel e Render publicam a partir do GitHub).
3. Conferir que nenhum segredo está versionado (`.env`, `appsettings.*.json` com chaves). A chave do Google Books e o `Jwt__Secret` entram **só** nas variáveis do Render.

## 1. Neon (banco)

1. Criar o projeto escolhendo **PostgreSQL 17** (o mesmo do `docker-compose.yml`; um dump de versão mais nova pode não restaurar em servidor mais antigo).
2. Copiar a string de conexão do endereço **direto** (sem `-pooler` no host). O pooler (PgBouncer) atrapalha migrations e `SET` por sessão.
3. Formato para o Npgsql (variável do Render):
   `Host=ep-xxx.sa-east-1.aws.neon.tech;Database=neondb;Username=...;Password=...;SSL Mode=Require`
4. Formato URI para o `pg_restore`: `postgresql://usuario:senha@ep-xxx.../neondb?sslmode=require`

### Levar os dados locais para o Neon

Sem este passo o banco novo nasce vazio (só o seed de países). O restore roda **dentro do contêiner `db`**, então não precisa instalar o PostgreSQL no Windows:

```powershell
.\scripts\backup-db.ps1                      # gera backups\atlas-AAAA-MM-DD_HHmm.dump
docker compose cp backups\atlas-XXXX.dump db:/tmp/restore.dump
docker compose exec -T db pg_restore --no-owner --no-acl --clean --if-exists -d "postgresql://USUARIO:SENHA@HOST_DIRETO/neondb?sslmode=require" /tmp/restore.dump
docker compose exec -T db rm -f /tmp/restore.dump
```

- O dump local já tem o histórico de migrations; ao iniciar, a API aplica só o que faltar (por exemplo `AddProfilesAndFollows`, se o backup for anterior a ela).
- Confira o tamanho antes: o plano gratuito do Neon tem cota pequena e as capas enviadas ficam **dentro do banco** (`book_cover`, até 2 MiB cada). Medir: `docker compose exec db psql -U atlas -d atlas_literario -c "select pg_size_pretty(pg_database_size('atlas_literario'));"`
- Vazamento: o dump contém e-mails e hashes de senha. Não o suba para o Git (`backups/` está no `.gitignore`).

## 2. Render (API)

Web Service → repositório do GitHub → **Language: Docker**, *Root Directory* `backend`, *Dockerfile Path* `Dockerfile`, branch `main`. Health check path: `/health`.

Variáveis de ambiente:

| Variável | Valor |
|---|---|
| `ConnectionStrings__Default` | string do Neon (item 1.3) |
| `Jwt__Secret` | **novo**, aleatório, ≥ 32 caracteres (nunca o de desenvolvimento). Trocar invalida as sessões. |
| `Database__ApplyMigrationsOnStartup` | `true` |
| `ASPNETCORE_HTTP_PORTS` | `10000` (o Dockerfile fixa 5080; o Render espera a porta que ele define) |
| `ForwardedHeaders__Enabled` | `true` (a API passa a enxergar HTTPS e o IP do cliente atrás do proxy) |
| `App__FrontendBaseUrl` | `https://SEU-PROJETO.vercel.app` |
| `Cors__AllowedOrigins__0` | `https://SEU-PROJETO.vercel.app` |
| `GoogleBooks__ApiKey` | a chave (restrinja-a no console do Google) |
| `ExternalSources__Contact` | e-mail de contato exigido por Open Library/Wikidata no `User-Agent` |
| `RateLimiting__AuthPerMinute` | opcional, padrão 30 (ver abaixo) |
| `Swagger__UiEnabled` | opcional; **não definir** em produção (a UI fica desligada em `Production`; o contrato `/openapi/v1.json` continua) |

`ASPNETCORE_ENVIRONMENT` não precisa ser definido: o padrão do contêiner é `Production`.

### Limites e comportamento

- **Render gratuito dorme** após ~15 min sem tráfego; a primeira requisição leva cerca de um minuto. Um monitor externo chamando `/health` a cada poucos minutos evita isso (respeitando os termos do plano).
- **Cliente espera a API acordar:** em produção o frontend consulta `/health` (com novas tentativas, até ~2 min) antes da primeira chamada e depois de ~10 min parado, e mostra "Acordando o servidor…". A sessão só é encerrada se o servidor rejeitar o refresh token (400/401/403); timeout ou 5xx mantêm o login. Ver `frontend/.../src/api/warmup.ts`.
- As migrations rodam na subida. **Faça backup antes** de publicar uma versão com migration nova.
- **Limite de tentativas** (login, cadastro, esqueci/redefinir senha): 30 por minuto **por IP** (`RateLimiting__AuthPerMinute`), resposta `429` com `Retry-After`. Atrás do Vercel + Render o IP vem de `X-Forwarded-For`; **verifique após publicar** que ele é o do visitante e não o do Vercel: se todas as requisições chegarem com o mesmo IP, o limite vira global (aumente o valor ou me avise para particionar por e-mail).

## 3. Vercel (frontend)

1. Importar o repositório; *Root Directory* `frontend/atlas-literario-web`; framework Vite; build `npm run build`; saída `dist`.
2. Variáveis de build: `VITE_USE_MOCKS=false` (explícito, para nunca embutir os mocks). Não definir `VITE_API_BASE_URL` (o padrão `/api` usa o rewrite).
3. Editar `frontend/atlas-literario-web/vercel.json` e trocar `SEU-SERVICO.onrender.com` pela URL real (já feito: `atlasliterario.onrender.com`) do serviço no Render **antes do primeiro deploy**; depois, o commit dispara o redeploy.
4. Os GeoJSON ficam em `public/geo` (~700 KB) e vão no próprio build; nada externo é carregado pelo mapa.

## 4. Verificação pós-publicação

1. `https://SEU-PROJETO.vercel.app/health` → `Healthy` (passa pelo rewrite até a API).
2. Entrar com a conta restaurada; abrir biblioteca, os dois mapas e o Dashboard.
3. Perfil: definir apelido e visibilidade, e testar a busca/seguir com duas contas (a Etapa 5 ainda aguarda esta validação em PostgreSQL real).
4. `https://SEU-SERVICO.onrender.com/swagger` não deve abrir; `/openapi/v1.json` sim.
5. Em *Logs* do Render, conferir que as migrations foram aplicadas e que não há `Jwt` vazio ou erro de conexão.

## 5. Rollback e manutenção

- Código: *Rollback* do deploy no painel do Render/Vercel. **Migrations não voltam sozinhas**: restaure o dump do Neon (ou use o *branch/restore* do próprio Neon) se uma migration der errado.
- Backup periódico a partir do Neon: `pg_dump` apontando para a URL do Neon, de dentro do contêiner `db`, no mesmo formato do script local.
- Tabelas que crescem sem limpeza: `external_lookup_cache` e tokens de refresh expirados (ainda sem rotina de expurgo).
