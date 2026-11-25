import { Fragment, type ReactNode } from 'react'
import { NavLink } from 'react-router'
import classes from './SideMenu.module.css'

export interface SideMenuSection {
  title?: ReactNode
  // An item without `to` is a page that doesn't exist yet: shown, not linked, marked `soon`
  items: { label: ReactNode; to?: string; end?: boolean }[]
}

// The » link list of a side Panel, in sections with a tinted heading
export function SideMenu({ sections, soon }: { sections: SideMenuSection[]; soon?: ReactNode }) {
  return (
    <>
      {sections.map((section, i) => (
        <Fragment key={i}>
          {section.title && <div className={classes.head}>{section.title}</div>}
          {section.items.map((item, j) =>
            item.to ? (
              <NavLink key={j} to={item.to} end={item.end} className={classes.item}>
                » {item.label}
              </NavLink>
            ) : (
              <span key={j} className={`${classes.item} ${classes.soon}`}>
                » {item.label}
                {soon && <> ({soon})</>}
              </span>
            ),
          )}
        </Fragment>
      ))}
    </>
  )
}

// The small bevelled tab buttons inside a Panel
export function MiniTabs<T extends string>({ value, onChange, tabs }: { value: T; onChange: (v: T) => void; tabs: { value: T; label: ReactNode }[] }) {
  return (
    <div className={classes.tabs}>
      {tabs.map((tab) => (
        <button key={tab.value} type="button" className={classes.tab} aria-pressed={tab.value === value} onClick={() => onChange(tab.value)}>
          {tab.label}
        </button>
      ))}
    </div>
  )
}
