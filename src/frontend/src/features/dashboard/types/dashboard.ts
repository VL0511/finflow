/** Fluxo liquidado (somente transações Completed) em um dia. */
export interface DailyCashFlow {
  /** Data local no formato YYYY-MM-DD. */
  date: string
  inflow: number
  outflow: number
}

/** Ponto já agregado e rotulado para o gráfico. */
export interface CashFlowPoint {
  key: string
  /** Rótulo curto do eixo (ex.: "05 nov", "nov"). */
  label: string
  /** Rótulo completo para o tooltip (ex.: "quinta, 05 de novembro"). */
  fullLabel: string
  inflow: number
  outflow: number
}

export type CashFlowPeriod = '7d' | '30d' | '12m'
