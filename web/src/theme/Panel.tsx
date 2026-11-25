import { Box, type BoxProps } from '@mantine/core'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import classes from './Panel.module.css'

// The site's box: a leather title bar, optionally with a game item icon (a path under
// /assets, like icons/items/200007.png), over a parchment body. Padding props go to the body,
// the rest (margins and such) to the box.
export function Panel({
  title,
  icon,
  aside,
  children,
  p = 'sm',
  px,
  py,
  pt,
  pb,
  ...box
}: {
  title: ReactNode
  icon?: string
  aside?: ReactNode
  children: ReactNode
} & BoxProps) {
  return (
    <Box className={classes.box} {...box}>
      <div className={classes.bar}>
        {icon && <img className={classes.icon} src={`/assets/${icon}`} alt="" onError={(e) => (e.currentTarget.style.display = 'none')} />}
        <span>{title}</span>
        {aside && <span className={classes.right}>{aside}</span>}
      </div>
      <Box className={classes.body} p={p} px={px} py={py} pt={pt} pb={pb}>
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
