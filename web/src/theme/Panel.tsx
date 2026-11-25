import { Box, type BoxProps } from '@mantine/core'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import classes from './Panel.module.css'

// The site's box: a leather title bar, optionally with a game item icon (a path under
// /assets, like icons/items/200007.png), over a parchment body
export function Panel({
  title,
  icon,
  right,
  children,
  p = 'sm',
  ...box
}: {
  title: ReactNode
  icon?: string
  right?: ReactNode
  children: ReactNode
} & BoxProps) {
  return (
    <Box className={classes.box} {...box}>
      <div className={classes.bar}>
        {icon && <img className={classes.icon} src={`/assets/${icon}`} alt="" onError={(e) => (e.currentTarget.style.display = 'none')} />}
        <span>{title}</span>
        {right && <span className={classes.right}>{right}</span>}
      </div>
      <Box className={classes.body} p={p}>
        {children}
      </Box>
    </Box>
  )
}

// The big gold or red button of the side column: a title and a line under it
export function BigButton({ to, color, title, note }: { to: string; color: 'gold' | 'red'; title: ReactNode; note?: ReactNode }) {
  return (
    <Link to={to} className={`${classes.big} ${classes[color]}`}>
      <span className={classes.bigTitle}>{title}</span>
      {note && <span className={classes.bigNote}>{note}</span>}
    </Link>
  )
}
