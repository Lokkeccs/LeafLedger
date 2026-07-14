import { useMemo, useReducer } from 'react'
import type { Account } from '../../application/accounts'
import type { JournalEntryInput } from '../../application/journalEntries'
import { balanceMirror, type MirrorIssue } from './balanceMirror'

export type JournalFormLine = { id: string; accountId: string; currency: string; amountMinor: number }
export type JournalEntryFormState = { date: string; description: string; reference: string; lines: JournalFormLine[] }
type Action = { type: 'field'; field: 'date' | 'description' | 'reference'; value: string } | { type: 'line'; id: string; patch: Partial<Omit<JournalFormLine, 'id'>> } | { type: 'add' } | { type: 'remove'; id: string }

const today = () => new Date().toISOString().slice(0, 10)
const newLine = (): JournalFormLine => ({ id: crypto.randomUUID(), accountId: '', currency: '', amountMinor: 0 })

function reducer(state: JournalEntryFormState, action: Action): JournalEntryFormState {
  if (action.type === 'field') return { ...state, [action.field]: action.value }
  if (action.type === 'add') return { ...state, lines: [...state.lines, newLine()] }
  if (action.type === 'remove') return { ...state, lines: state.lines.filter((line) => line.id !== action.id) }
  return { ...state, lines: state.lines.map((line) => line.id === action.id ? { ...line, ...action.patch } : line) }
}

export function useJournalEntryForm(accounts: Account[]) {
  const [state, dispatch] = useReducer(reducer, undefined, () => ({ date: today(), description: '', reference: '', lines: [newLine(), newLine()] }))
  const issues = useMemo<MirrorIssue[]>(() => balanceMirror(state.description, state.lines, accounts), [accounts, state.description, state.lines])
  const input = useMemo<JournalEntryInput>(() => ({ date: state.date, description: state.description, reference: state.reference || null, lines: state.lines.map(({ accountId, currency, amountMinor }) => ({ accountId, currency, amountMinor })) }), [state])
  return { state, issues, input, isValid: issues.length === 0, setField: (field: 'date' | 'description' | 'reference', value: string) => dispatch({ type: 'field', field, value }), updateLine: (id: string, patch: Partial<Omit<JournalFormLine, 'id'>>) => dispatch({ type: 'line', id, patch }), addLine: () => dispatch({ type: 'add' }), removeLine: (id: string) => dispatch({ type: 'remove', id }) }
}