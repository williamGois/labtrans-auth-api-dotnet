# Auth API .NET

Microsservico ASP.NET Core Web API responsavel por cadastro, login, hash seguro de senha, emissao de JWT e rota autenticada `/api/auth/me`.

## Tecnologias

- .NET 8
- ASP.NET Core Controllers
- Entity Framework Core + PostgreSQL/Npgsql
- JWT Bearer
- BCrypt.Net-Next
- xUnit + WebApplicationFactory
- prometheus-net
- OpenTelemetry opcional via OTLP

## Variaveis

Configure no ambiente ou copie `.env.example`:

```env
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5001
AUTH_DB_CONNECTION_STRING=
JWT_SECRET=
JWT_ISSUER=labtrans-auth-api
JWT_AUDIENCE=labtrans-reservas
JWT_EXPIRES_MINUTES=60
CORS_ORIGINS=http://localhost:5173,http://127.0.0.1:5173
OTEL_SERVICE_NAME=labtrans-auth-api-dotnet
OTEL_TRACES_EXPORTER=none
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```

`AUTH_DB_CONNECTION_STRING` e `JWT_SECRET` devem ser definidos localmente por variavel de ambiente. `JWT_SECRET` precisa ter pelo menos 32 bytes.

## Instalar e Rodar

```powershell
dotnet restore
dotnet ef database update
dotnet run --urls http://localhost:5001
```

URLs:

- Live health: `http://localhost:5001/health/live`
- Ready health: `http://localhost:5001/health/ready`
- Metrics: `http://localhost:5001/metrics`
- Swagger: `http://localhost:5001/swagger`

## Migrations

```powershell
dotnet ef migrations add NomeDaMigration
dotnet ef database update
```

No Docker Compose, a API aplica migrations automaticamente quando `APPLY_MIGRATIONS=true`.

## Testes

```powershell
dotnet test tests\AuthApi.Tests\AuthApi.Tests.csproj --collect:"XPlat Code Coverage"
dotnet format AuthApi.csproj --verify-no-changes
dotnet list AuthApi.csproj package --vulnerable --include-transitive
```

Cobertura funcional atual:

- Health check.
- Registro com sucesso.
- Registro com e-mail invalido, senha vazia e senha curta.
- Registro duplicado retorna `409`.
- Login valido retorna JWT.
- Login com senha incorreta retorna `401`.
- Login com usuario inexistente retorna `401`.
- JWT contem ID do usuario, e-mail, issuer, audience e expiracao.
- Senha nao e salva em texto puro e e verificavel por BCrypt.
- `/api/auth/me` exige token e retorna usuario autenticado com token valido.

## Endpoints

### GET `/health/live`

Retorna:

```json
{
  "status": "ok",
  "service": "labtrans-auth-api-dotnet",
  "correlationId": "..."
}
```

### GET `/health/ready`

Valida banco e configuracao obrigatoria:

```json
{
  "status": "ready",
  "checks": {
    "database": "ok",
    "configuration": "ok"
  },
  "correlationId": "..."
}
```

### GET `/metrics`

Expoe metricas Prometheus, incluindo `http_requests_total`, `auth_login_success_total`, `auth_login_failure_total` e `auth_jwt_issued_total`.

### POST `/api/auth/register`

```json
{
  "email": "usuario@email.com",
  "password": "<senha-de-desenvolvimento>"
}
```

Retorna usuario sem senha.

### POST `/api/auth/login`

```json
{
  "email": "usuario@email.com",
  "password": "<senha-de-desenvolvimento>"
}
```

Retorna:

```json
{
  "accessToken": "...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "user": {
    "id": "...",
    "email": "usuario@email.com"
  }
}
```

### GET `/api/auth/me`

Rota protegida por:

```text
Authorization: Bearer <token>
```

## Observabilidade e Operacao

- Toda resposta devolve `X-Correlation-ID`.
- Se o cliente enviar `X-Correlation-ID`, o valor e preservado.
- Erros usam envelope com `title`, `status`, `detail`, `correlationId` e `timestamp`.
- Logs sao emitidos em JSON e nao incluem senha, token JWT completo, secret ou connection string.
- Runbook operacional: `docs/OPERATIONS.md`.
- Decisao arquitetural: `docs/ADR-001-observability-strategy.md`.
- Relatorio: `OBSERVABILITY_REPORT.md`.

## Security Notes

- Secrets reais nao sao versionados.
- `.env.example` mantem `AUTH_DB_CONNECTION_STRING` e `JWT_SECRET` sem valores.
- `AUTH_DB_CONNECTION_STRING` e `JWT_SECRET` devem ser definidos localmente ou no ambiente de execucao.
- `JWT_SECRET` deve ter no minimo 32 bytes em desenvolvimento.
- O projeto possui varredura de secrets com Gitleaks via workflow dedicado.
- Nunca commite `.env`, tokens, chaves privadas, connection strings preenchidas ou dumps locais.
