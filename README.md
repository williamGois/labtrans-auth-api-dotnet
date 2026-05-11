# Auth API .NET

Microsservico ASP.NET Core Web API responsavel por cadastro, login, hash seguro de senha, emissao de JWT e rota autenticada `/api/auth/me`.

## Tecnologias

- .NET 8
- ASP.NET Core Controllers
- Entity Framework Core + PostgreSQL/Npgsql
- JWT Bearer
- BCrypt.Net-Next
- xUnit + WebApplicationFactory

## Variaveis

Configure no ambiente ou copie `.env.example`:

```env
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5001
AUTH_DB_CONNECTION_STRING=AUTH_DB_CONNECTION_STRING_REDACTED
JWT_SECRET=JWT_SECRET_PLACEHOLDER
JWT_ISSUER=labtrans-auth-api
JWT_AUDIENCE=labtrans-reservas
JWT_EXPIRES_MINUTES=60
CORS_ORIGINS=http://localhost:5173,http://127.0.0.1:5173
```

`JWT_SECRET` precisa ter pelo menos 32 bytes e deve vir do ambiente em execucao real. O valor acima e apenas placeholder.

## Instalar e Rodar

```powershell
dotnet restore
dotnet ef database update
dotnet run --urls http://localhost:5001
```

URLs:

- Health: `http://localhost:5001/health`
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

### GET `/health`

Retorna:

```json
{ "status": "ok" }
```

### POST `/api/auth/register`

```json
{
  "email": "usuario@email.com",
  "password": "TEST_CREDENTIAL_REDACTED"
}
```

Retorna usuario sem senha.

### POST `/api/auth/login`

```json
{
  "email": "usuario@email.com",
  "password": "TEST_CREDENTIAL_REDACTED"
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
