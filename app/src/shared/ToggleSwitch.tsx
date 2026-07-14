import type { ReactNode } from 'react'
import styles from './ToggleSwitch.module.css'

export interface ToggleSwitchProps { checked: boolean; onChange: (checked: boolean) => void; label: ReactNode; disabled?: boolean }

export function ToggleSwitch({ checked, onChange, label, disabled }: ToggleSwitchProps) {
  return <label className={`${styles.root}${disabled ? ` ${styles.disabled}` : ''}`}>
    <span className={styles.control}><input className={styles.input} type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} disabled={disabled} /><span className={styles.track}><span className={styles.thumb} /></span></span>
    <span className={styles.label}>{label}</span>
  </label>
}