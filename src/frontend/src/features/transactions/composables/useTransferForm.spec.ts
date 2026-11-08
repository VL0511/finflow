import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick } from 'vue'
import { accountsApi } from '@/features/accounts/api/accountsApi'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import { ApiError } from '@/shared/api'
import { ana, bruno, carla, diego, makeTransaction, paged } from '@/test/fixtures'
import { transactionsApi } from '../api/transactionsApi'
import type { TransferPreview } from '../types/transaction'
import { PREVIEW_DEBOUNCE_MS, useTransferForm } from './useTransferForm'

vi.mock('../api/transactionsApi', () => ({
  transactionsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getStatus: vi.fn(), create: vi.fn(), cancel: vi.fn(), preview: vi.fn() },
}))
vi.mock('@/features/accounts/api/accountsApi', () => ({
  accountsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getBalance: vi.fn(), cashFlow: vi.fn(), create: vi.fn() },
}))

const txApi = vi.mocked(transactionsApi)
const accApi = vi.mocked(accountsApi)

function previewOf(overrides: Partial<TransferPreview> = {}): TransferPreview {
  return {
    sourceCurrency: 'BRL',
    destinationCurrency: 'BRL',
    amount: 10,
    destinationAmount: 10,
    exchangeRate: 1,
    exchangeRateId: null,
    isConversion: false,
    destinationHolder: 'Bruno L.',
    ...overrides,
  }
}

async function setup(initial?: { sourceAccount?: string }) {
  accApi.list.mockResolvedValue(paged([ana, bruno, carla, diego], { pageSize: 100 }))
  accApi.getBalance.mockResolvedValue({ accountId: ana.id, accountNumber: '12345', balance: 500, currency: 'BRL' })
  await useAccountsStore().fetchDirectory()
  const scope = effectScope()
  const form = scope.run(() => useTransferForm(initial))!
  return { form, scope }
}

/** Preenche e deixa a prévia (com debounce) terminar. */
async function fill(form: ReturnType<typeof useTransferForm>, source: string, destination: string, amount: number | null) {
  form.values.sourceAccount = source
  form.values.destinationAccount = destination
  form.values.amount = amount
  await nextTick()
  await vi.advanceTimersByTimeAsync(PREVIEW_DEBOUNCE_MS)
}

describe('useTransferForm', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('consulta o saldo atual da origem (pré-selecionada via query)', async () => {
    const { form } = await setup({ sourceAccount: '12345' })
    await vi.waitFor(() => expect(form.sourceBalance.value?.balance).toBe(500))
    expect(accApi.getBalance).toHaveBeenCalledWith(ana.id, expect.any(AbortSignal))
  })

  it('valida localmente: obrigatórios, contas iguais e valor; não pede prévia nem envia', async () => {
    const { form } = await setup()
    await fill(form, '12345', '12345', 0)

    await expect(form.review()).resolves.toBe(false)

    expect(txApi.preview).not.toHaveBeenCalled()
    // Origem igual ao destino: o destino é limpo automaticamente (a mesma conta não pode receber).
    expect(form.errors.value).toEqual({
      destinationAccount: 'Selecione a conta de destino.',
      amount: 'O valor deve ser maior que zero.',
    })
    expect(form.step.value).toBe('edit')
  })

  it('prévia: calculada pelo backend, com debounce (uma requisição para várias digitações)', async () => {
    txApi.preview.mockResolvedValue(previewOf({ isConversion: true, destinationCurrency: 'USD', destinationAmount: 1.92, exchangeRate: 0.192 }))
    const { form } = await setup()
    form.values.sourceAccount = '12345'
    form.values.destinationAccount = '11223'
    for (const amount of [1, 10, 10.5, 10]) {
      form.values.amount = amount
      await nextTick()
      await vi.advanceTimersByTimeAsync(100)
    }
    await vi.advanceTimersByTimeAsync(PREVIEW_DEBOUNCE_MS)

    expect(txApi.preview).toHaveBeenCalledTimes(1)
    expect(txApi.preview).toHaveBeenCalledWith({ sourceAccount: '12345', destinationAccount: '11223', amount: 10 }, expect.any(AbortSignal))
    expect(form.preview.value?.destinationAmount).toBe(1.92)
  })

  it('prévia: resposta antiga não sobrescreve a mais recente', async () => {
    let resolveOld: (p: TransferPreview) => void = () => {}
    txApi.preview
      .mockReturnValueOnce(new Promise((r) => (resolveOld = r)))
      .mockResolvedValueOnce(previewOf({ amount: 20, destinationAmount: 20 }))
    const { form } = await setup()

    await fill(form, '12345', '67890', 10)   // 1ª prévia fica pendente
    await fill(form, '12345', '67890', 20)   // 2ª responde
    resolveOld(previewOf({ amount: 10, destinationAmount: 10 }))
    await vi.advanceTimersByTimeAsync(0)

    expect(form.preview.value?.amount).toBe(20)
  })

  it('destino: só contas cadastradas e ativas, sem a própria origem', async () => {
    const { form } = await setup()
    form.values.sourceAccount = '12345'
    await nextTick()

    // diego (44556) está inativa; ana (12345) é a origem.
    expect(form.destinationOptions.value.map((a) => a.accountNumber)).toEqual(['67890', '11223'])
  })

  it('destino fora das contas cadastradas é recusado sem pedir prévia', async () => {
    const { form } = await setup()
    await fill(form, '12345', '9999999999', 10)

    await expect(form.review()).resolves.toBe(false)

    expect(txApi.preview).not.toHaveBeenCalled()
    expect(form.errors.value.destinationAccount).toBe('Selecione uma conta de destino cadastrada.')
  })

  it('trocar a origem para a conta escolhida como destino limpa o destino', async () => {
    const { form } = await setup()
    form.values.sourceAccount = '12345'
    form.values.destinationAccount = '67890'
    await nextTick()

    form.values.sourceAccount = '67890'
    await nextTick()

    expect(form.values.destinationAccount).toBe('')
  })

  it('revisar → confirmar envia SÓ contas e valor (taxa e convertido são recalculados pela API)', async () => {
    txApi.preview.mockResolvedValue(previewOf({ isConversion: true, destinationCurrency: 'USD', destinationAmount: 192.31, exchangeRate: 0.19231 }))
    txApi.create.mockResolvedValue(makeTransaction({ id: 'tx-9' }))
    const { form } = await setup()
    await fill(form, '12345', '11223', 1000)

    await expect(form.review()).resolves.toBe(true)
    expect(form.step.value).toBe('confirm')

    const result = await form.confirm()

    expect(txApi.create).toHaveBeenCalledWith({ sourceAccount: '12345', destinationAccount: '11223', amount: 1000 })
    expect(result?.id).toBe('tx-9')
  })

  it('alterar algo na confirmação volta para a edição e invalida a prévia', async () => {
    txApi.preview.mockResolvedValue(previewOf())
    const { form } = await setup()
    await fill(form, '12345', '67890', 10)
    await form.review()

    form.values.amount = 11
    await nextTick()

    expect(form.step.value).toBe('edit')
    expect(form.preview.value).toBeNull()
  })

  it('sem taxa vigente (422 na prévia): mostra o erro e não avança', async () => {
    const semTaxa = new ApiError({ kind: 'http', status: 422, title: 'Regra de negócio violada.', detail: 'Não há taxa de câmbio vigente de BRL para USD.' })
    txApi.preview.mockRejectedValue(semTaxa)
    const { form } = await setup()
    await fill(form, '12345', '11223', 10)

    await expect(form.review()).resolves.toBe(false)

    expect(form.previewError.value).toBe(semTaxa)
    expect(form.step.value).toBe('edit')
  })

  it('não envia duas vezes com cliques repetidos', async () => {
    txApi.preview.mockResolvedValue(previewOf())
    let resolve: (v: ReturnType<typeof makeTransaction>) => void = () => {}
    txApi.create.mockReturnValue(new Promise((r) => (resolve = r)))
    const { form } = await setup()
    await fill(form, '12345', '67890', 10)
    await form.review()

    const first = form.confirm()
    const second = await form.confirm()
    resolve(makeTransaction())
    await first

    expect(second).toBeNull()
    expect(txApi.create).toHaveBeenCalledTimes(1)
  })

  it('422 na confirmação volta para a edição com o erro', async () => {
    txApi.preview.mockResolvedValue(previewOf())
    const error = new ApiError({ kind: 'http', status: 422, title: 'Regra de negócio violada.', detail: 'Conta de destino não encontrada.' })
    txApi.create.mockRejectedValue(error)
    const { form } = await setup()
    await fill(form, '12345', '67890', 10)
    await form.review()

    await form.confirm()

    expect(form.submitError.value).toBe(error)
    expect(form.step.value).toBe('edit')
  })

  it('avisa (sem bloquear) sobre saldo insuficiente', async () => {
    txApi.preview.mockResolvedValue(previewOf())
    const { form } = await setup({ sourceAccount: '12345' })
    await vi.waitFor(() => expect(form.sourceBalance.value).not.toBeNull())

    await fill(form, '12345', '67890', 600)

    expect(form.warnings.value).toEqual(['O valor é maior que o saldo atual da origem: o processamento deve rejeitar por saldo insuficiente.'])
  })
})
