import { PublicClientApplication, type AccountInfo } from '@azure/msal-browser'
import { msalConfig } from './msalConfig'

export const msalInstance = new PublicClientApplication(msalConfig)

let initialization: Promise<void> | undefined
let interactiveOperation: Promise<unknown> | undefined
let authenticatedAccount: AccountInfo | null = null

export function initializeMsal(): Promise<void> {
  initialization ??= msalInstance.initialize().then(() => {
    if (!msalInstance.getActiveAccount()) {
      const cachedAccount = msalInstance.getAllAccounts()[0]
      if (cachedAccount) {
        msalInstance.setActiveAccount(cachedAccount)
        authenticatedAccount = cachedAccount
      }
    }
  })
  return initialization
}

export function getAuthenticatedAccount(): AccountInfo | null {
  return authenticatedAccount ?? msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0] ?? null
}

export function rememberAuthenticatedAccount(account: AccountInfo | null): void {
  authenticatedAccount = account
}

export function runMsalInteraction<T>(operation: () => Promise<T>): Promise<T> {
  if (interactiveOperation) return interactiveOperation as Promise<T>

  const pending = operation()
  interactiveOperation = pending
  const clearInteraction = () => {
    if (interactiveOperation === pending) interactiveOperation = undefined
  }
  void pending.then(clearInteraction, clearInteraction)
  return pending
}