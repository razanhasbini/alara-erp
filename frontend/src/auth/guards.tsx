import { Navigate, Outlet, useLocation } from "react-router"
import { useAuth } from "@/auth/auth"
import { FullPageSpinner } from "@/components/common/states"

/** Requires a signed-in employee; forces a password change when an admin issued a temporary one. */
export function RequireAuth() {
  const { user, isLoading } = useAuth()
  const location = useLocation()
  if (isLoading) return <FullPageSpinner />
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  if (user.mustChangePassword && location.pathname !== "/profile")
    return <Navigate to="/profile" replace state={{ forced: true }} />
  return <Outlet />
}

/** UX-only gate; the API independently enforces the same permission. */
export function RequirePermission({ permission }: { permission: string }) {
  const { can } = useAuth()
  return can(permission) ? <Outlet /> : <Navigate to="/unauthorized" replace />
}
