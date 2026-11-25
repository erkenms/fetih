import { Avatar, type AvatarProps } from '@mantine/core'
import { playerAvatarUrl } from './format'

interface PlayerAvatarProps extends Omit<AvatarProps, 'src' | 'alt'> {
  avatar: number
  name: string
}

export function PlayerAvatar({ avatar, name, ...rest }: PlayerAvatarProps) {
  return (
    <Avatar src={playerAvatarUrl(avatar)} alt={name} radius={0} color="red" style={{ border: '1px solid #5a3a1a' }} {...rest}>
      {name.slice(0, 1).toUpperCase()}
    </Avatar>
  )
}
