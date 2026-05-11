# Auth API Operations

## Health

```powershell
curl http://localhost:5001/health/live
curl http://localhost:5001/health/ready
```

- `/health/live` valida somente que o processo responde.
- `/health/ready` valida banco e configuracao obrigatoria.
- Resposta nao retorna secrets ou connection string.

## Metrics

```powershell
curl http://localhost:5001/metrics
```

Metricas principais:

- `http_requests_total`
- `http_request_duration_seconds`
- `http_requests_in_progress`
- `auth_register_success_total`
- `auth_register_failure_total`
- `auth_login_success_total`
- `auth_login_failure_total`
- `auth_jwt_issued_total`
- `auth_invalid_credentials_total`
- `auth_protected_route_unauthorized_total`

## Logs

Os logs saem em JSON no console. Campos importantes:

- `serviceName`
- `environment`
- `correlationId`
- `method`
- `path`
- `route`
- `statusCode`
- `elapsedMs`
- `userId`, quando autenticado

Senha, `PasswordHash`, token JWT, `Authorization` completo, `JWT_SECRET` e connection string nao sao logados.

## Correlation ID

Envie um header:

```text
X-Correlation-ID: suporte-123
```

O servico devolve o mesmo valor no response header. Se o cliente nao enviar, a API gera um GUID.

## Diagnostico

### 401 em rota protegida

1. Verifique se existe `Authorization: Bearer <token>`.
2. Confirme `JWT_ISSUER`, `JWT_AUDIENCE` e `JWT_SECRET`.
3. Consulte logs filtrando por `correlationId`.
4. Consulte `auth_protected_route_unauthorized_total`.

### Falha de banco

1. Execute `/health/ready`.
2. Verifique `AUTH_DB_CONNECTION_STRING`.
3. Confirme se o container PostgreSQL da Auth API esta ativo.
4. Consulte logs com `statusCode=503` ou eventos de excecao.

## OpenTelemetry

Tracing e opcional. Para ativar:

```env
OTEL_SERVICE_NAME=labtrans-auth-api-dotnet
OTEL_TRACES_EXPORTER=otlp
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```

Sem `OTEL_TRACES_EXPORTER=otlp`, o servico roda normalmente sem exportar traces.

## Testes

Sem SDK .NET local, use Docker:

```powershell
docker run --rm -v ${PWD}:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet restore
docker run --rm -v ${PWD}:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet build AuthApi.csproj
docker run --rm -v ${PWD}:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/AuthApi.Tests/AuthApi.Tests.csproj
```
