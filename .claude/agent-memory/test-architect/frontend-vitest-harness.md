---
name: frontend-vitest-harness
description: How the estuary-frontend test harness works — vitest config, required mocks, and jsdom polyfills for writing new tests
metadata:
  type: reference
---

# estuary-frontend test harness (vitest)

Repo: `estuary-product/estuary-frontend`. Runner is **vitest** (not jest). Package manager **pnpm**.

- Run: `pnpm test:run` (= `vitest run`). Typecheck: `pnpm check` (= `tsc`).
- Config: `vitest.config.ts` — jsdom, `globals: true`, setup `src/test/setup.ts`, aliases `@`→`src`, `@shared`→`shared`.
- **Include glob is `src/**/*.test.{ts,tsx}`** → every test file MUST live under `src/`. To test `shared/schema.ts`, put the test under `src/` (e.g. `src/config/schema.test.ts`) and import from `@shared/schema` or `../../shared/schema`.
- `tsconfig.json` **excludes `**/*.test.ts(x)`**, so `pnpm check`/`tsc` does NOT typecheck test files. Only non-test source (incl. `src/test/setup.ts`) is checked.

## Required mocks / gotchas
- Anything importing `@/lib/queryClient` (getAuthHeaders/apiRequest/getBackendUrl/getStaticUrl, and `@/lib/utils` which re-exports getStaticUrl) pulls in Firebase. Mock it: `vi.mock("@/lib/firebase", ...)` + `vi.mock("firebase/auth", ...)`. Canonical pattern lives in `src/hooks/useAgentMutation.orgheader.test.tsx`. The firebase/auth `onAuthStateChanged` stub logs a harmless TDZ `unsubscribe` warning to stderr — getAuthToken catches it and returns null; tests still pass.
- `setActiveOrgId(null)` from queryClient resets org-header state between tests.
- Multipart create/update POSTs build a `FormData` you can introspect with `body.get(key)` / `body.has(key)` in jsdom.
- Partial-mock `@/lib/utils` with `importOriginal` to keep real `defaultAvatarImages` while stubbing `getAvatarUrl`/`getStaticUrl` (pattern in `agent-card.test.tsx`).
- react-hook-form components: wrap in `<FormProvider {...form}>` (shadcn `FormItem/FormMessage` call `useFormContext`).
- Mock `@tanstack/react-query` with `importOriginal` and override only `useQuery` — the bare object breaks `QueryClient` used elsewhere in the import chain.

## jsdom polyfills (src/test/setup.ts)
Already polyfills pointer-capture, `scrollIntoView`, and (added) **`ResizeObserver`** — Radix `ScrollArea` needs ResizeObserver in a layout effect or it throws "ResizeObserver is not defined" on interaction.

## Known PRE-EXISTING failing tests (not caused by new work, as of 2026-07-22)
Source defaults drifted from these committed tests: `src/config/tts-config.test.ts` (expects ttsTemperature 1.1, source is 0.85), `src/lib/transcript-export.test.ts`, `src/components/character-info-popover.test.tsx`, and `src/components/onboarding/OnboardingSurveyModal.test.tsx` (14, ~1s each). Verify against a clean baseline before assuming your change broke them.
