import { Component, type ErrorInfo, type ReactNode } from 'react'
import { i18n } from '../i18n'

interface AppErrorBoundaryProps { children: ReactNode; onError?: (error: Error, errorInfo: ErrorInfo) => void }
interface AppErrorBoundaryState { error: Error | null }

export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  state: AppErrorBoundaryState = { error: null }

  static getDerivedStateFromError(error: Error): AppErrorBoundaryState { return { error } }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    this.props.onError?.(error, errorInfo)
    console.error('LeafLedger app shell error', error)
  }

  render() {
    if (this.state.error) {
      return <main className="error-shell" role="alert"><p className="eyebrow">LeafLedger</p><h1>{i18n.t('shell.appErrorTitle')}</h1><p>{i18n.t('shell.appErrorDetail')}</p><button type="button" onClick={() => window.location.reload()}>{i18n.t('shell.reloadApplication')}</button></main>
    }
    return this.props.children
  }
}