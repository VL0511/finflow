import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import type { AccountBalanceResponse } from '@/features/accounts/types/account'
import { useCurrenciesStore } from '@/features/currencies/stores/useCurrenciesStore'
import { isApiError, normalizeApiError, type ApiError } from '@/shared/api'
import { transactionsApi } from '../api/transactionsApi'
import { useTransactionsStore } from '../stores/useTransactionsStore'
import type { CreateTransactionRequest, Transaction, TransferPreview } from '../types/transaction'

type Field = 'sourceAccount' | 'destinationAccount' | 'amount'
export type TransferStep = 'edit' | 'confirm'

/** Espera após a última digitação antes de pedir a prévia ao backend. */
export const PREVIEW_DEBOUNCE_MS = 400

/**
 * Formulário de nova transferência em duas etapas: preencher → revisar/confirmar.
 *
 * - Origem: contas do escopo do usuário (o Customer só vê as dele).
 * - Destino: seletor de contas CADASTRADAS e ativas visíveis ao usuário (Admin/Treasurer: todas;
 *   Customer: as próprias — a API não expõe contas de outros clientes).
 * - Valor: número normalizado pela máscara (MoneyInput), na moeda da origem.
 * - Prévia (moedas, taxa, valor a receber, titular mascarado): SEMPRE calculada pelo backend
 *   (POST /transactions/preview). O frontend nunca calcula câmbio nem envia taxa/valor convertido:
 *   na confirmação a API recalcula tudo com a taxa vigente naquele momento.
 */
export function useTransferForm(initial: { sourceAccount?: string } = {}) {
  const accounts = useAccountsStore()
  const currencies = useCurrenciesStore()
  const transactions = useTransactionsStore()

  const values = reactive({
    sourceAccount: initial.sourceAccount ?? '',
    destinationAccount: '',
    amount: null as number | null,
  })

  const step = ref<TransferStep>('edit')
  const errors = ref<Partial<Record<Field, string>>>({})
  const submitError = shallowRef<ApiError | null>(null)
  const isSubmitting = ref(false)

  const source = computed(() => accounts.findByNumber(values.sourceAccount) ?? null)
  const sourceCurrency = computed(() => source.value?.currency ?? 'BRL')
  const decimals = computed(() => currencies.decimalsFor(source.value?.currency))
  /** Destino escolhido no seletor: precisa ser uma conta cadastrada e visível ao usuário. */
  const destination = computed(() => accounts.findByNumber(values.destinationAccount) ?? null)

  /** Contas que podem receber: cadastradas, ativas e diferentes da origem. */
  const destinationOptions = computed(() =>
    accounts.directory.filter((a) => a.isActive && a.accountNumber !== values.sourceAccount),
  )

  // ---- saldo da origem ----------------------------------------------------

  const sourceBalance = shallowRef<AccountBalanceResponse | null>(null)
  const sourceBalanceError = shallowRef<ApiError | null>(null)
  const isLoadingBalance = ref(false)
  let balanceRequest: AbortController | null = null

  async function loadSourceBalance() {
    balanceRequest?.abort()
    sourceBalance.value = null
    sourceBalanceError.value = null
    const account = source.value
    if (!account) return

    const controller = new AbortController()
    balanceRequest = controller
    isLoadingBalance.value = true
    try {
      sourceBalance.value = await accounts.fetchBalance(account.id, controller.signal)
    } catch (error) {
      const apiError = normalizeApiError(error)
      if (apiError.kind !== 'canceled') sourceBalanceError.value = apiError
    } finally {
      if (balanceRequest === controller) {
        isLoadingBalance.value = false
        balanceRequest = null
      }
    }
  }

  watch(() => source.value?.id, () => void loadSourceBalance(), { immediate: true })

  // Origem passou a ser a conta que estava como destino: o destino deixa de ser válido.
  watch(
    () => values.sourceAccount,
    (sourceAccount) => {
      if (sourceAccount && sourceAccount === values.destinationAccount) values.destinationAccount = ''
    },
  )

  // ---- prévia calculada pelo backend ------------------------------------------

  const preview = shallowRef<TransferPreview | null>(null)
  const previewError = shallowRef<ApiError | null>(null)
  const isLoadingPreview = ref(false)
  let previewController: AbortController | null = null
  let previewRequestId = 0
  let debounceTimer: ReturnType<typeof setTimeout> | undefined

  function currentRequest(): CreateTransactionRequest | null {
    const amount = values.amount
    if (!values.sourceAccount || !destination.value || amount === null || amount <= 0) return null
    if (destination.value.accountNumber === values.sourceAccount) return null
    return { sourceAccount: values.sourceAccount, destinationAccount: destination.value.accountNumber, amount }
  }

  /** Pede a prévia; respostas de pedidos anteriores são descartadas. */
  async function loadPreview(): Promise<TransferPreview | null> {
    clearTimeout(debounceTimer)
    previewController?.abort()
    const request = currentRequest()
    preview.value = null
    previewError.value = null
    if (!request) return null

    const requestId = ++previewRequestId
    const controller = new AbortController()
    previewController = controller
    isLoadingPreview.value = true
    try {
      const result = await transactionsApi.preview(request, controller.signal)
      if (requestId !== previewRequestId) return null
      preview.value = result
      return result
    } catch (error) {
      const apiError = isApiError(error) ? error : normalizeApiError(error)
      if (requestId === previewRequestId && apiError.kind !== 'canceled') previewError.value = apiError
      return null
    } finally {
      if (requestId === previewRequestId) isLoadingPreview.value = false
    }
  }

  // Qualquer alteração invalida a prévia (e volta da confirmação para a edição).
  watch(
    () => [values.sourceAccount, values.destinationAccount, values.amount] as const,
    () => {
      step.value = 'edit'
      preview.value = null
      previewError.value = null
      clearTimeout(debounceTimer)
      if (currentRequest()) debounceTimer = setTimeout(() => void loadPreview(), PREVIEW_DEBOUNCE_MS)
    },
  )

  // ---- avisos (não bloqueiam: o Worker decide no processamento) -------------------

  const warnings = computed(() => {
    const list: string[] = []
    if (source.value && !source.value.isActive) list.push('A conta de origem está inativa: o processamento deve rejeitar a transferência.')
    const balance = sourceBalance.value?.balance
    if (balance !== undefined && values.amount !== null && values.amount > balance) {
      list.push('O valor é maior que o saldo atual da origem: o processamento deve rejeitar por saldo insuficiente.')
    }
    return list
  })

  // ---- validação, revisão e envio -----------------------------------------------

  function validate(): CreateTransactionRequest | null {
    const next: Partial<Record<Field, string>> = {}
    if (!values.sourceAccount) next.sourceAccount = 'Conta de origem obrigatória.'
    if (!values.destinationAccount) next.destinationAccount = 'Selecione a conta de destino.'
    else if (values.destinationAccount === values.sourceAccount) next.destinationAccount = 'As contas devem ser diferentes.'
    else if (!destination.value) next.destinationAccount = 'Selecione uma conta de destino cadastrada.'
    else if (!destination.value.isActive) next.destinationAccount = 'A conta de destino está inativa.'

    if (values.amount === null) next.amount = 'Informe o valor.'
    else if (values.amount <= 0) next.amount = 'O valor deve ser maior que zero.'

    errors.value = next
    return Object.keys(next).length > 0 ? null : currentRequest()
  }

  /** Etapa 1 → 2: valida e obtém a prévia atualizada do backend antes de mostrar a confirmação. */
  async function review(): Promise<boolean> {
    submitError.value = null
    if (!validate()) return false
    const result = preview.value ?? (await loadPreview())
    if (!result) return false
    step.value = 'confirm'
    return true
  }

  function backToEdit() {
    step.value = 'edit'
  }

  function applyServerError(error: ApiError) {
    const fieldMessages: Partial<Record<Field, string>> = {}
    for (const field of ['sourceAccount', 'destinationAccount', 'amount'] as const) {
      const message = error.fieldError(field)
      if (message) fieldMessages[field] = message
    }
    errors.value = fieldMessages
    submitError.value = Object.keys(fieldMessages).length > 0 ? null : error
    step.value = 'edit'
  }

  /** Etapa 2: envia. Devolve a transação aceita (Pending) ou `null`. Nunca envia duas vezes em paralelo. */
  async function confirm(): Promise<Transaction | null> {
    if (isSubmitting.value) return null
    submitError.value = null
    const request = validate()
    if (!request) {
      step.value = 'edit'
      return null
    }

    isSubmitting.value = true
    try {
      // Só o pedido (contas e valor): taxa e valor convertido são recalculados pela API.
      return await transactions.createTransfer(request)
    } catch (error) {
      applyServerError(isApiError(error) ? error : normalizeApiError(error))
      return null
    } finally {
      isSubmitting.value = false
    }
  }

  return {
    values,
    step,
    errors,
    submitError,
    isSubmitting,
    source,
    sourceCurrency,
    decimals,
    destination,
    destinationOptions,
    sourceBalance,
    sourceBalanceError,
    isLoadingBalance,
    preview,
    previewError,
    isLoadingPreview,
    warnings,
    loadPreview,
    review,
    backToEdit,
    confirm,
  }
}
