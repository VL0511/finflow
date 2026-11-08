import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/shared/api'
import { makeAccount } from '@/test/fixtures'
import { accountsApi } from '../api/accountsApi'
import { useCreateAccountForm } from './useCreateAccountForm'

vi.mock('../api/accountsApi', () => ({
  accountsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getBalance: vi.fn(), cashFlow: vi.fn(), create: vi.fn() },
}))

const api = vi.mocked(accountsApi)

describe('useCreateAccountForm', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('valida localmente e não chama a API com dados inválidos', async () => {
    const form = useCreateAccountForm()
    form.values.holderName = '  '
    form.values.initialBalance = -5

    await expect(form.submit()).resolves.toBeNull()

    expect(api.create).not.toHaveBeenCalled()
    expect(form.errors.value).toMatchObject({
      holderName: 'Titular obrigatório.',
      initialBalance: 'O saldo inicial não pode ser negativo.',
    })
  })

  it('envia o payload normalizado SEM número de conta (gerado pelo backend)', async () => {
    api.create.mockResolvedValue(makeAccount({ accountNumber: '4820193756' }))
    const form = useCreateAccountForm()
    form.values.holderName = '  Fernanda Lima '
    form.values.initialBalance = 1500.5
    form.values.currency = 'EUR'

    const created = await form.submit()

    expect(api.create).toHaveBeenCalledWith({
      holderName: 'Fernanda Lima',
      initialBalance: 1500.5,
      currency: 'EUR',
      isActive: true,
      ownerUserId: null,
    })
    expect(api.create.mock.calls[0]![0]).not.toHaveProperty('accountNumber')
    expect(created?.accountNumber).toBe('4820193756')
  })

  it('saldo em branco é enviado como zero', async () => {
    api.create.mockResolvedValue(makeAccount())
    const form = useCreateAccountForm()
    form.values.holderName = 'Ana'

    await form.submit()

    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ initialBalance: 0 }))
  })

  it('mapeia erros de validação do backend (400) por campo', async () => {
    api.create.mockRejectedValue(
      new ApiError({ kind: 'http', status: 400, title: 'Dados inválidos.', fieldErrors: { holderName: ['Titular obrigatório.'] } }),
    )
    const form = useCreateAccountForm()
    form.values.holderName = 'A'

    await form.submit()

    expect(form.errors.value.holderName).toBe('Titular obrigatório.')
    expect(form.submitError.value).toBeNull()
  })

  it('422 (moeda desativada) aparece no alerta do formulário', async () => {
    const rejected = new ApiError({ kind: 'http', status: 422, title: 'Regra de negócio violada.', detail: "A moeda 'EUR' está desativada." })
    api.create.mockRejectedValue(rejected)
    const form = useCreateAccountForm()
    form.values.holderName = 'A'

    await form.submit()

    expect(form.submitError.value).toBe(rejected)
  })

  it('erro sem campo (rede) vai para o alerta do formulário', async () => {
    const network = new ApiError({ kind: 'network', status: 0, title: 'Não foi possível conectar ao servidor.' })
    api.create.mockRejectedValue(network)
    const form = useCreateAccountForm()
    form.values.holderName = 'A'

    await form.submit()

    expect(form.submitError.value).toBe(network)
    expect(form.isSubmitting.value).toBe(false)
  })
})
