import { useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import styles from './ModalShell.module.css'

export interface ModalShellProps {
  open?: boolean
  onClose?: () => void
  closeLabel?: string
  title?: ReactNode
  titleId?: string
  children: ReactNode
  footer?: ReactNode
  maxWidth?: number | string
  zIndex?: number
  center?: boolean
}

export function ModalShell({ open = true, onClose, closeLabel, title, titleId, children, footer, maxWidth = 560, zIndex = 500, center = false }: ModalShellProps) {
  const backdropMouseDown = useRef(false)
  const generatedTitleId = useId()
  if (!open) return null
  const hasHeader = title != null || onClose != null
  const labelledBy = title != null ? titleId ?? generatedTitleId : undefined

  return createPortal(<div role="dialog" aria-modal="true" aria-labelledby={labelledBy} className={styles.overlay} style={{ zIndex, alignItems: center ? 'center' : undefined }} onMouseDown={(event) => { backdropMouseDown.current = event.target === event.currentTarget }} onClick={(event) => { if (backdropMouseDown.current && event.target === event.currentTarget) onClose?.() }}>
    <div className={styles.panel} style={{ maxWidth }}>
      {hasHeader && <div className={styles.header}>{title != null && <h2 id={labelledBy} className={styles.headerTitle}>{title}</h2>}{onClose != null && <button type="button" aria-label={closeLabel} onClick={onClose}>×</button>}</div>}
      <div className={styles.body} data-testid="modal-body">{children}</div>
      {footer != null && <div className={styles.footer}>{footer}</div>}
    </div>
  </div>, document.body)
}