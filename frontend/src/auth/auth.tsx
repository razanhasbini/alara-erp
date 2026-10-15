import { createContext, useCallback, useContext, useEffect, useMemo, type ReactNode } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { api, ApiError, onUnauthorized, refreshCsrf } from "@/lib/api"
import type { CurrentUser } from "@/types/api"

interface AuthState {
  user: CurrentUser | null
  isLoading: boolean
  can: (permission: string) => boolean
  login: (email: string, password: string) => Promise<CurrentUser>
  logout: () => Promise<void>
  refresh: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)
const ME = ["auth", "me"] as const

async function fetchMe(): Promise<CurrentUser | null> {
  try {
    return await api<CurrentUser>("/api/auth/me")
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) return null
    throw e
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const { data: user = null, isLoading } = useQuery({ queryKey: ME, queryFn: fetchMe, staleTime: 60_000, retry: false })

  /**
   * Switch identity: drop every cached business query (it belonged to the previous user) but keep the
   * auth query itself, so its subscribers see the new value. Clearing everything would orphan them.
   */
  const setIdentity = useCallback((me: CurrentUser | null) => {
    queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== ME[0] })
    queryClient.setQueryData(ME, me)
  }, [queryClient])

  // A 401 from any request means the session ended (expired, deactivated, password reset elsewhere).
  useEffect(() => {
    onUnauthorized(() => setIdentity(null))
  }, [setIdentity])

  const login = useCallback(async (email: string, password: string) => {
    const me = await api<CurrentUser>("/api/auth/login", { method: "POST", body: { email, password } })
    await refreshCsrf()
    setIdentity(me)
    return me
  }, [setIdentity])

  const logout = useCallback(async () => {
    try {
      await api("/api/auth/logout", { method: "POST" })
    } finally {
      await refreshCsrf().catch(() => undefined)
      setIdentity(null)
    }
  }, [setIdentity])

  const refresh = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: ME })
  }, [queryClient])

  const value = useMemo<AuthState>(() => {
    const permissions = new Set(user?.permissions ?? [])
    return { user, isLoading, can: (p) => permissions.has(p), login, logout, refresh }
  }, [user, isLoading, login, logout, refresh])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>")
  return ctx
}
