import { MantineProvider } from '@mantine/core'
import { Notifications } from '@mantine/notifications'
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { lazy, type ComponentType } from 'react'
import { createBrowserRouter, RouterProvider, type RouteObject } from 'react-router'
import { ApiError } from '../api/client'
import { AdminLayout } from './AdminLayout'
import type { FeatureRoute } from './feature'
import { NotFound, RequirePermission } from './Guards'
import { AdminHome, PublicHome } from './HomePages'
import { LoginPage } from './LoginPage'
import { PublicLayout } from './PublicLayout'
import { featureRoutes } from './routes'
import { cssVariablesResolver, theme } from '../theme/theme'

// A 401 anywhere means the session ended; dropping the cached account sends guards to login
function expireSessionOn401(error: unknown) {
  if (error instanceof ApiError && error.status === 401) void queryClient.invalidateQueries({ queryKey: ['me'] })
}

const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError: expireSessionOn401 }),
  mutationCache: new MutationCache({ onError: expireSessionOn401 }),
  defaultOptions: {
    queries: {
      retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
    },
  },
})

const pages = new Map<FeatureRoute, ComponentType>()
function toRoute(feature: FeatureRoute): RouteObject {
  let Page = pages.get(feature)
  if (!Page) pages.set(feature, (Page = lazy(feature.page)))
  return {
    path: feature.path,
    handle: { bare: feature.bare },
    element: feature.permission ? (
      <RequirePermission permission={feature.permission}>
        <Page />
      </RequirePermission>
    ) : (
      <Page />
    ),
  }
}

const publicHomeRoute = featureRoutes.find((r) => r.area === 'public' && r.path === '')

const router = createBrowserRouter([
  {
    path: '/admin',
    element: (
      <RequirePermission permission="AdminArea">
        <AdminLayout />
      </RequirePermission>
    ),
    children: [
      { index: true, handle: { bare: true }, element: <AdminHome /> },
      ...featureRoutes.filter((r) => r.area === 'admin').map(toRoute),
      { path: '*', element: <NotFound /> },
    ],
  },
  {
    path: '/',
    element: <PublicLayout />,
    children: [
      {
        index: true,
        handle: { bare: publicHomeRoute?.bare },
        element: publicHomeRoute ? toRoute(publicHomeRoute).element : <PublicHome />,
      },
      { path: 'login', element: <LoginPage /> },
      ...featureRoutes.filter((r) => r.area === 'public' && r.path !== '').map(toRoute),
      { path: '*', element: <NotFound /> },
    ],
  },
])

export function App() {
  return (
    <MantineProvider theme={theme} cssVariablesResolver={cssVariablesResolver} forceColorScheme="light">
      <Notifications />
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>
  )
}
