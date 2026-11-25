import { describe, expect, it } from 'vitest'
import {
  applyMapDelta,
  hpPercent,
  pkNameStyle,
  snapshotToState,
  summarizeActiveMaps,
  type LiveMapDelta,
  type LiveMapSnapshot,
  type LiveOnlinePlayer,
} from './liveState'

describe('liveState', () => {
  const baseSnapshot: LiveMapSnapshot = {
    mapId: 1002,
    width: 1000,
    height: 1000,
    takenAt: '2026-11-25T06:00:00Z',
    gameOnline: true,
    exists: true,
    players: [
      {
        characterID: 1,
        name: 'Alice',
        level: 50,
        class: 15,
        x: 430,
        y: 378,
        pkPoints: 0,
        nameColor: 'white',
        dead: false,
        health: 500,
        maxHealth: 500,
        mesh: 1003,
      },
      {
        characterID: 2,
        name: 'Bob',
        level: 80,
        class: 45,
        x: 440,
        y: 380,
        pkPoints: 35,
        nameColor: 'red',
        dead: false,
        health: 900,
        maxHealth: 1000,
        mesh: 1004,
      },
    ],
    monsters: [
      {
        id: 10,
        typeID: 1,
        name: 'Pheasant',
        level: 1,
        lookface: 100,
        x: 300,
        y: 300,
        alive: true,
        health: 50,
        maxHealth: 50,
        guard: false,
      },
    ],
    groundItems: [
      {
        id: 100,
        typeId: 1088001,
        itemName: 'Meteor',
        icon: null,
        x: 305,
        y: 305,
        silver: 0,
        expiresAt: '2026-11-25T06:01:00Z',
      },
    ],
    booths: [
      {
        id: 500,
        ownerID: 1,
        ownerName: 'Alice',
        x: 432,
        y: 378,
        items: 3,
        hawk: 'Selling Mets',
      },
    ],
  }

  it('indexes snapshot entities by id and applies deltas cleanly', () => {
    const state = snapshotToState(baseSnapshot)
    expect(Object.keys(state.players)).toEqual(['1', '2'])
    expect(state.groundItems[100]?.itemName).toBe('Meteor')

    const delta: LiveMapDelta = {
      mapId: 1002,
      takenAt: '2026-11-25T06:00:01Z',
      gameOnline: true,
      playersUpsert: [{ ...baseSnapshot.players[0], x: 435, y: 379 }],
      playersRemoved: [2],
      monstersUpsert: [{ ...baseSnapshot.monsters[0], health: 10 }],
      monstersRemoved: [],
      groundItemsUpsert: [
        {
          id: 101,
          typeId: 1090000,
          itemName: null,
          icon: null,
          x: 306,
          y: 306,
          silver: 1200,
          expiresAt: '2026-11-25T06:01:05Z',
        },
      ],
      groundItemsRemoved: [100],
      boothsUpsert: [],
      boothsRemoved: [500],
    }

    const next = applyMapDelta(state, delta)
    expect(next.players[1]?.x).toBe(435)
    expect(next.players[2]).toBeUndefined()
    expect(next.monsters[10]?.health).toBe(10)
    expect(next.groundItems[100]).toBeUndefined()
    expect(next.groundItems[101]?.silver).toBe(1200)
    expect(next.booths[500]).toBeUndefined()
  })

  it('ignores deltas for a different map', () => {
    const state = snapshotToState(baseSnapshot)
    const next = applyMapDelta(state, {
      mapId: 1036,
      takenAt: '2026-11-25T06:00:01Z',
      gameOnline: true,
      playersUpsert: [],
      playersRemoved: [1],
      monstersUpsert: [],
      monstersRemoved: [],
      groundItemsUpsert: [],
      groundItemsRemoved: [],
      boothsUpsert: [],
      boothsRemoved: [],
    })
    expect(next).toBe(state)
    expect(next.players[1]).toBeDefined()
  })

  it('computes HP percentages and PK styles safely', () => {
    expect(hpPercent(50, 100)).toBe(50)
    expect(hpPercent(150, 100)).toBe(100)
    expect(hpPercent(10, 0)).toBe(0)
    expect(pkNameStyle('red').badgeColor).toBe('red')
    expect(pkNameStyle('black').badgeColor).toBe('dark')
    expect(pkNameStyle('blue').badgeColor).toBe('blue')
    expect(pkNameStyle('white').badgeColor).toBe('gray')
  })

  it('summarizes active maps sorted by player count descending', () => {
    const players: LiveOnlinePlayer[] = [
      { characterId: 1, accountId: 1, name: 'A', level: 10, class: 10, rebirths: 0, mapId: 1002, mapName: 'TwinCity', x: 1, y: 1, pkPoints: 0, authority: 1, ipAddress: '127.0.0.1', loggedInAt: null },
      { characterId: 2, accountId: 2, name: 'B', level: 20, class: 20, rebirths: 0, mapId: 1036, mapName: 'Market', x: 1, y: 1, pkPoints: 0, authority: 1, ipAddress: '127.0.0.1', loggedInAt: null },
      { characterId: 3, accountId: 3, name: 'C', level: 30, class: 40, rebirths: 0, mapId: 1002, mapName: 'TwinCity', x: 2, y: 2, pkPoints: 0, authority: 1, ipAddress: '127.0.0.1', loggedInAt: null },
    ]
    expect(summarizeActiveMaps(players)).toEqual([
      { mapId: 1002, mapName: 'TwinCity', count: 2 },
      { mapId: 1036, mapName: 'Market', count: 1 },
    ])
  })
})
