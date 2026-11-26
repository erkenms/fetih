# fetih web

The web app's frontend: Vite, React, TypeScript, Mantine, TanStack Query, React Router,
react-i18next. The backend is `src/Fetih.Web`. The plan is issue #37. The look is set by
[docs/web-style.md](../docs/web-style.md).

```bash
cd src/Fetih.Web && dotnet run   # backend on 127.0.0.1:5820
cd web && npm install && npm run dev   # http://localhost:5173, proxies /api and /assets
npm run build                          # into web/dist, which Fetih.Web serves
npm test                               # Vitest, pure logic only
npm run api                            # regenerate src/api/schema.d.ts after a backend change
```

Log in with the seed account `test` / `test`. The admin panel needs authority 2 or more
(`UPDATE account SET AuthorityID = 5 WHERE Username = 'test'` in `fetih_account`).

## Adding a feature

Copy `features/health` (and `Fetih.Web/Features/Health` for the backend):

1. Backend: `Fetih.Web/Features/<Name>/<Name>Feature.cs` implements `IFeature`; add it to
   `Features/FeatureRegistry.cs`. Guard endpoints with `.RequirePermission(Permission.X)`,
   return typed records, and answer errors with `ApiError.Result("code", status)`.
   Changes go through `AuditLog.WriteAsync`; game calls through `GameRpc.CallAsync`.
2. `npm run api` so the frontend sees the new endpoints in `src/api/schema.d.ts`.
3. Frontend: `src/features/<name>/index.ts` exports a `FeatureRoute`, added to
   `src/app/routes.tsx`. Pages call `unwrap(api.GET('/api/...'))` inside `useQuery`.
4. Text: `src/locales/en/<name>.json` and `src/locales/tr/<name>.json` with the same keys
   (a test checks), used with `useTranslation('<name>')`. Error codes get a text in
   `common.json` under `errors`.

Client art is under `/assets/...`, built by `tools/assets`; pages must still work without it.
