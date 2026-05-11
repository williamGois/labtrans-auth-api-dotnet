# ADR-001 - Observability Strategy

## Contexto

A Auth API emite o JWT usado pelos demais servicos. Problemas de autenticacao precisam ser rastreaveis sem expor senha, token ou secret.

## Decisao

- Usar `X-Correlation-ID` para correlacionar front-end, Auth API e Reservations API.
- Produzir logs estruturados em JSON com contexto operacional minimo.
- Expor `/health/live` para vida do processo e `/health/ready` para prontidao com banco/configuracao.
- Expor metricas Prometheus em `/metrics`.
- Ativar OpenTelemetry por variavel de ambiente, sem tornar o fluxo local obrigatoriamente dependente de collector.
- Padronizar erros com `title`, `status`, `detail`, `correlationId` e `timestamp`.

## Seguranca

Nao sao logados:

- Senhas.
- Hash de senha completo.
- JWT completo.
- Header `Authorization`.
- `JWT_SECRET`.
- Connection string.

## Consequencias

O avaliador consegue diagnosticar login, registro, tokens invalidos, latencia e disponibilidade sem precisar alterar codigo ou consultar banco manualmente.
