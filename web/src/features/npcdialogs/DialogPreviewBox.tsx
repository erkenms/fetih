import { Anchor, Badge, Code, Stack, Text } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import type { Schemas } from '../../api/client'
import classes from './NpcDialogsPage.module.css'
import { optionKindColor } from './npcDialogUtils'

export function DialogPreviewBox({
  page,
  index,
  fare,
}: {
  page: Schemas['NpcDialogPage']
  index: number
  fare?: number | null
}) {
  const { t } = useTranslation('npcDialogs')

  return (
    <div className={classes.dialogBox}>
      <div className={classes.dialogHeader}>
        <span>{t('preview.page', { n: index + 1 })}</span>
        {page.face > 0 && (
          <Text size="xs" c="dimmed">
            {t('preview.face', { face: page.face })}
          </Text>
        )}
      </div>

      <div className={classes.dialogBody}>
        {page.facePath && (
          <img
            className={classes.portrait}
            src={`/assets/${page.facePath}`}
            alt=""
            onError={(e) => (e.currentTarget.style.display = 'none')}
          />
        )}
        <Stack gap={4} style={{ flex: 1, minWidth: 0 }}>
          {page.lines.length > 0 && (
            <div className={classes.dialogLines}>
              {page.lines.map((line, i) => (
                <Text key={i} size="xs" mb={4} style={{ whiteSpace: 'pre-wrap' }}>
                  {line}
                </Text>
              ))}
            </div>
          )}

          {page.options.length > 0 && (
            <Stack gap={2}>
              {page.options.map((opt, idx) => (
                <div key={`${opt.option}-${idx}`} className={classes.optionRow}>
                  <Badge size="xs" color={optionKindColor(opt.kind)}>
                    {t(`optionKind.${opt.kind}` as never)}
                  </Badge>
                  <Text size="xs" fw={600}>
                    {opt.text}
                  </Text>
                  {opt.option != null && <Code fz="xs">#{opt.option}</Code>}
                  {opt.targetMapId != null && (
                    <Anchor
                      component={Link}
                      to={`/admin/maps/${opt.targetMapId}${opt.targetX != null && opt.targetY != null ? `?x=${opt.targetX}&y=${opt.targetY}` : ''}`}
                      size="xs"
                    >
                      → {opt.targetMapName ?? `#${opt.targetMapId}`} ({opt.targetX}, {opt.targetY})
                    </Anchor>
                  )}
                  {opt.targetMapId != null && fare != null && fare > 0 && (
                    <Text size="xs" c="dimmed">
                      ({t('preview.fare', { fare })})
                    </Text>
                  )}
                </div>
              ))}
            </Stack>
          )}
        </Stack>
      </div>
    </div>
  )
}
