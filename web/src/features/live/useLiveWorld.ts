import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import {
  applyMapDelta,
  emptyMapState,
  snapshotToState,
  type LiveMapDelta,
  type LiveMapSnapshot,
  type LiveMapState,
  type LiveOnlinePlayer,
  type OnlinePlayersResponse,
} from './liveState'

export type HubStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

export interface UseLiveWorldResult {
  status: HubStatus
  gameOnline: boolean
  onlinePlayers: LiveOnlinePlayer[]
  mapState: LiveMapState
}

export function useLiveWorld(mapId: number): UseLiveWorldResult {
  const [status, setStatus] = useState<HubStatus>('connecting')
  const [gameOnline, setGameOnline] = useState(false)
  const [onlinePlayers, setOnlinePlayers] = useState<LiveOnlinePlayer[]>([])
  const [mapState, setMapState] = useState<LiveMapState>(() => emptyMapState(mapId))

  const connRef = useRef<HubConnection | null>(null)
  const activeMapIdRef = useRef(mapId)
  activeMapIdRef.current = mapId

  useEffect(() => {
    let disposed = false
    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/live')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    connRef.current = conn

    const subscribeAll = async () => {
      if (disposed || conn.state !== HubConnectionState.Connected) return
      const targetMapId = activeMapIdRef.current
      try {
        const [onlineResp, mapSnap] = await Promise.all([
          conn.invoke<OnlinePlayersResponse>('SubscribeOnline'),
          conn.invoke<LiveMapSnapshot>('SubscribeMap', targetMapId),
        ])
        if (disposed) return
        setGameOnline(onlineResp.gameOnline || mapSnap.gameOnline)
        setOnlinePlayers(onlineResp.players)
        if (activeMapIdRef.current === mapSnap.mapId) {
          setMapState(snapshotToState(mapSnap))
        }
      } catch {
        // Reconnect handler will retry if the socket dropped mid-invoke
      }
    }

    conn.on('Snapshot', (snapshot: LiveMapSnapshot) => {
      if (disposed) return
      setGameOnline(snapshot.gameOnline)
      if (snapshot.mapId === activeMapIdRef.current) {
        setMapState(snapshotToState(snapshot))
      }
    })

    conn.on('Delta', (delta: LiveMapDelta) => {
      if (disposed) return
      setGameOnline(delta.gameOnline)
      if (delta.mapId === activeMapIdRef.current) {
        setMapState((prev) => applyMapDelta(prev, delta))
      }
    })

    conn.on('OnlinePlayers', (response: OnlinePlayersResponse) => {
      if (disposed) return
      setGameOnline(response.gameOnline)
      setOnlinePlayers(response.players)
    })

    conn.on('GameStatus', (online: boolean) => {
      if (disposed) return
      setGameOnline(online)
      if (!online) {
        setOnlinePlayers([])
        setMapState((prev) => ({
          ...emptyMapState(prev.mapId),
          gameOnline: false,
        }))
      }
    })

    conn.onreconnecting(() => {
      if (!disposed) setStatus('reconnecting')
    })

    conn.onreconnected(() => {
      if (disposed) return
      setStatus('connected')
      void subscribeAll()
    })

    conn.onclose(() => {
      if (!disposed) setStatus('disconnected')
    })

    const start = async () => {
      try {
        await conn.start()
        if (disposed) {
          await conn.stop()
          return
        }
        setStatus('connected')
        await subscribeAll()
      } catch {
        if (!disposed) setStatus('disconnected')
      }
    }

    void start()

    return () => {
      disposed = true
      connRef.current = null
      void conn.stop()
    }
  }, [])

  // Switch map subscription when mapId changes while connected
  useEffect(() => {
    setMapState(emptyMapState(mapId))
    const conn = connRef.current
    if (!conn || conn.state !== HubConnectionState.Connected) return

    let cancelled = false
    void conn
      .invoke<LiveMapSnapshot>('SubscribeMap', mapId)
      .then((snap) => {
        if (cancelled || activeMapIdRef.current !== snap.mapId) return
        setGameOnline(snap.gameOnline)
        setMapState(snapshotToState(snap))
      })
      .catch(() => {})

    return () => {
      cancelled = true
    }
  }, [mapId])

  return { status, gameOnline, onlinePlayers, mapState }
}
