import { PublicClientApplication } from '@azure/msal-browser'
import { msalConfig } from './msalConfig'

export const msalInstance = new PublicClientApplication(msalConfig)

let initialization: Promise<void> | undefined

export function initializeMsal(): Promise<void> {
  initialization ??= msalInstance.initialize()
  return initialization
}