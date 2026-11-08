# FinFlow — Frontend

Vue 3 + TypeScript + Vite + Pinia + Tailwind. Consome a FinFlow.API (ASP.NET Core) — **sem dados mockados**:
tudo o que aparece na tela vem da API; se ela falhar, a tela mostra o erro (não há fallback silencioso).

## Rodando em desenvolvimento

```bash
docker compose up -d                                   # SQL Server + RabbitMQ (raiz do repositório)
dotnet run --project src/FinFlow.API --launch-profile http   # API em http://localhost:5017
npm install && npm run dev                             # frontend em http://localhost:5173
```

O Vite encaminha `/api` para `http://localhost:5017` (ver `vite.config.ts`). Para outra URL, defina
`VITE_API_BASE_URL` em `.env.local`.

Sem o **Worker** rodando, transferências ficam em `Pending` — a interface mostra isso honestamente
(o acompanhamento expira e oferece "Verificar novamente"), nunca presume a conclusão.

| Script | O que faz |
|---|---|
| `npm run dev` | Servidor de desenvolvimento |
| `npm run build` | Type-check (`vue-tsc`) + build de produção |
| `npm test` | Testes unitários (Vitest; API mockada, sem rede) |

## Arquitetura

```
src/
  shared/api/            httpClient (Axios), ApiError, normalizeApiError, useRequestState (loading/erro + deduplicação)
  shared/utils/poller.ts polling controlado (backoff, timeout, pausa com aba oculta, sem execuções paralelas)
  features/<feature>/
    api/                 único lugar com URLs e chamadas HTTP da feature
    stores/              estado (Pinia) — fonte de verdade, alimentada só pela API
    composables/         lógica reutilizável de tela (formulários, detalhes, polling)
    components/ views/   apresentação
    types/               espelho dos DTOs do backend
```

- Leituras de lista passam por `useRequestState().run`: chamadas simultâneas compartilham a mesma requisição.
- Mutações (`createAccount`, `createTransfer`, `cancelTransaction`) rejeitam com `ApiError`; os formulários
  mapeiam `errors` do `ValidationProblemDetails` para os campos.

## Endpoints utilizados

Listagens são **paginadas no servidor** (`page` padrão 1, `pageSize` padrão 10, máximo 100) e devolvem
`{ items, page, pageSize, totalItems, totalPages, hasPreviousPage, hasNextPage }`. Totais e indicadores
nunca são calculados a partir de uma página: vêm dos endpoints de resumo.

| Endpoint | Uso |
|---|---|
| `GET /api/accounts?page&pageSize` | Tela de contas (paginada); diretório de seletores (1 página de 100) |
| `GET /api/accounts/summary` | Saldo total por moeda, quantidade de contas (dashboard e tela de contas) |
| `GET /api/accounts/{id}` | Detalhe da conta |
| `GET /api/accounts/{id}/balance` | "Consultar saldo", saldo da origem na transferência, saldos após conclusão |
| `GET /api/accounts/{id}/cash-flow?from&to&utcOffsetMinutes` | Gráfico de entradas/saídas do dashboard (agregado por dia no banco) |
| `POST /api/accounts` | Nova conta (400 por campo, 409 número duplicado) |
| `GET /api/transactions?page&pageSize&status&accountNumber` | Histórico (filtro de status no servidor), movimentações da conta, recentes |
| `GET /api/transactions/summary` | Contagem por status: abas do histórico, menu lateral, KPIs, taxa de sucesso |
| `GET /api/transactions/{id}` | Detalhe da transação |
| `GET /api/transactions/{id}/status` | Polling do processamento assíncrono |
| `POST /api/transactions` | Nova transferência (202 → `Pending`; 400 por campo; 422 conta inexistente) |
| `PATCH /api/transactions/{id}/cancel` | Cancelamento (só `Pending`; 409 recarrega o status real) |

### Paginação no frontend

`shared/composables/usePagedList.ts` controla página, tamanho (10/20/50) e filtros de cada listagem:
mudar filtro ou tamanho volta à página 1; respostas antigas são descartadas (a requisição superada é
cancelada); requisições idênticas em voo são reaproveitadas; se a página deixar de existir (ex.: após um
cancelamento) a lista vai para a última página real. O histórico e a tela de contas guardam esse estado no
store, então página e filtro são preservados ao abrir um detalhe e voltar.

## Processamento assíncrono

1. `POST /api/transactions` responde **202** com a transação em `Pending` → a tela de detalhe abre.
2. O detalhe consulta `GET /status` em intervalos crescentes (1 s → 5 s) até `Completed`, `Failed` ou `Cancelled`;
   desiste após 2 min de tempo ativo ou 3 falhas seguidas, pausa com a aba oculta e para ao sair da tela.
3. Ao observar `Completed`, os saldos das duas contas são reconsultados (`GET /balance`).
4. Histórico e dashboard recarregam a página exibida e o resumo (5 s → 15 s) enquanto o resumo indicar
   transações em andamento no sistema, por até 3 min; a contagem no menu lateral vem do resumo.

## Limitações do backend (documentadas, não simuladas)

- **Contas não podem ser editadas nem excluídas** — a API só expõe criação e consulta.
- **Sem histórico de saldo** — o saldo total não tem tendência/sparkline nem comparação com períodos anteriores.
- **Sem dono de conta / workspace** — não existe "minhas contas" vs. "contrapartes". Entradas e saídas só fazem
  sentido por conta (no sistema todo, cada transferência é entrada e saída ao mesmo tempo), por isso o gráfico
  do dashboard tem seletor de conta.
- **Seletores limitados a 100 contas** — formulário de transferência e gráfico do dashboard usam uma página de
  100 contas (máximo da API); acima disso um aviso é exibido. Para volumes maiores: endpoint de busca de contas.
- **Sem índices para o filtro por conta** — `accountNumber` filtra por `SourceAccount`/`DestinationAccount`,
  colunas sem índice; com muitos registros, vale uma migration com índices nessas colunas.
- **Sem conversão cambial** — saldos em USD/EUR aparecem à parte do total em BRL.
- **Sem autenticação** — o usuário do cabeçalho é ilustrativo.
