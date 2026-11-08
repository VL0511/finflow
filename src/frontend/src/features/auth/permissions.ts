/**
 * Perfis e capacidades — ESPELHO das policies do backend (FinFlow.API/Security/SecuritySetup.cs),
 * usado só para mostrar/esconder menus e rotas. Não é mecanismo de segurança: a API reaplica
 * todas as permissões em cada requisição, e o perfil vem da sessão no servidor.
 */
export type Role = 'Administrator' | 'Treasurer' | 'Customer'

export const ROLE_LABELS: Record<Role, string> = {
  Administrator: 'Administrador',
  Treasurer: 'Tesouraria',
  Customer: 'Cliente',
}

export type Capability =
  /** Contas, saldos e transferências no próprio escopo (policy Banking). */
  | 'banking'
  /** Abrir conta (policy AccountOpening). */
  | 'openAccount'
  /** Visão de todas as contas da instituição (policy Operations). */
  | 'operations'
  /** Usuários, moedas, taxas e auditoria (policy Administration). */
  | 'administration'

const CAPABILITIES: Record<Role, readonly Capability[]> = {
  Administrator: ['banking', 'openAccount', 'operations', 'administration'],
  Treasurer: ['banking', 'operations'],
  Customer: ['banking', 'openAccount'],
}

export function isRole(value: string | null | undefined): value is Role {
  return value === 'Administrator' || value === 'Treasurer' || value === 'Customer'
}

export function can(role: string | null | undefined, capability: Capability): boolean {
  return isRole(role) && CAPABILITIES[role].includes(capability)
}
