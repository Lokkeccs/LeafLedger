import { Component, type ErrorInfo, type ReactNode } from 'react'

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
      return <main className="error-shell" role="alert"><p className="eyebrow">LeafLedger</p><h1>Something went wrong</h1><p>The application could not load this workspace.</p><button type="button" onClick={() => window.location.reload()}>Reload application</button></main>
    }
    return this.props.children
  }
}