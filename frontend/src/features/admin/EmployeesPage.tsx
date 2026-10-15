import { useState } from "react"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { MoreHorizontal, Plus } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { ConfirmDialog, Field, SearchInput, Segmented, SubmitButton } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { ActivePill, Pill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import { api, ApiError, errorMessage } from "@/lib/api"
import { when } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk } from "@/lib/queries"
import type { Paged, Role, UserSummary } from "@/types/api"
import { passwordRule } from "@/lib/rules"

function generatePassword() {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789"
  const bytes = crypto.getRandomValues(new Uint32Array(12))
  const body = Array.from(bytes, (b) => chars[b % chars.length]).join("")
  return `${body.slice(0, 4)}-${body.slice(4, 8)}-${body.slice(8)}A7` // guarantees upper + digit
}

export function EmployeesPage() {
  const { can, user: me } = useAuth()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState("")
  const [status, setStatus] = useState<"active" | "inactive" | "all">("active")
  const [page, setPage] = useState(1)
  const [dialog, setDialog] = useState<{ kind: "new" } | { kind: "edit" | "roles" | "reset"; user: UserSummary } | null>(null)
  const [toggle, setToggle] = useState<UserSummary | null>(null)
  const query = { search, isActive: status === "all" ? undefined : status === "active", page, pageSize: 25 }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.users(query), queryFn: () => api<Paged<UserSummary>>("/api/users", { query }), placeholderData: keepPreviousData,
  })
  const setActive = useMutation({
    mutationFn: (u: UserSummary) => api(`/api/users/${u.id}/${u.isActive ? "deactivate" : "activate"}`, { method: "POST" }),
    onSuccess: async (_, u) => { await queryClient.invalidateQueries({ queryKey: ["identity"] }); setToggle(null); toast.success(u.isActive ? `${u.firstName} deactivated and signed out` : `${u.firstName} reactivated`) },
    onError: (e) => { setToggle(null); toast.error(errorMessage(e)) },
  })

  const columns: Column<UserSummary>[] = [
    { key: "name", header: "Employee", cell: (u) => <div><div className="font-medium">{u.firstName} {u.lastName}{u.id === me?.id && <span className="ml-1.5 text-xs text-muted-foreground">(you)</span>}</div><div className="text-xs text-muted-foreground">{u.email}</div></div> },
    { key: "roles", header: "Roles", cell: (u) => <div className="flex flex-wrap gap-1">{u.roles.map((r) => <Pill key={r} tone={r === "Super Admin" ? "rose" : "neutral"} dot={false}>{r}</Pill>)}</div> },
    { key: "last", header: "Last sign-in", hideOnMobile: true, cell: (u) => <span className="text-muted-foreground">{u.lastLoginAt ? when(u.lastLoginAt) : "Never"}</span> },
    { key: "status", header: "Status", cell: (u) => <div className="flex gap-1"><ActivePill active={u.isActive} />{u.isLockedOut && <Pill tone="amber" dot={false}>Locked</Pill>}</div> },
    {
      key: "actions", header: "", align: "right",
      cell: (u) => u.id === me?.id || !(can(P.users.update) || can(P.users.assignRoles)) ? null : (
        <DropdownMenu>
          <DropdownMenuTrigger asChild><Button variant="ghost" size="icon-sm" aria-label={`Actions for ${u.firstName}`}><MoreHorizontal /></Button></DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {can(P.users.update) && <DropdownMenuItem onSelect={() => setDialog({ kind: "edit", user: u })}>Edit details</DropdownMenuItem>}
            {can(P.users.assignRoles) && <DropdownMenuItem onSelect={() => setDialog({ kind: "roles", user: u })}>Change roles</DropdownMenuItem>}
            {can(P.users.update) && <DropdownMenuItem onSelect={() => setDialog({ kind: "reset", user: u })}>Reset password{u.isLockedOut ? " & unlock" : ""}</DropdownMenuItem>}
            {can(P.users.update) && <><DropdownMenuSeparator /><DropdownMenuItem variant={u.isActive ? "destructive" : "default"} onSelect={() => setToggle(u)}>{u.isActive ? "Deactivate" : "Reactivate"}</DropdownMenuItem></>}
          </DropdownMenuContent>
        </DropdownMenu>
      ),
    },
  ]

  return (
    <>
      <PageHeader title="Employees" description="People who can sign in to the ERP. There is no public sign-up; accounts are created here."
        actions={can(P.users.create) && <Button onClick={() => setDialog({ kind: "new" })}><Plus /> Add employee</Button>} />
      <div className="mb-3 flex flex-wrap gap-2">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1) }} placeholder="Name or email" />
        <Segmented label="Status" value={status} onChange={(v) => { setStatus(v); setPage(1) }}
          options={[{ value: "active", label: "Active" }, { value: "inactive", label: "Deactivated" }, { value: "all", label: "All" }]} />
      </div>
      <DataTable columns={columns} rows={data?.items} rowKey={(u) => u.id} loading={isLoading} error={error} onRetry={refetch}
        empty={<EmptyState title="No employees found" />}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={setPage} />} />

      {toggle && (
        <ConfirmDialog open onOpenChange={(o) => !o && setToggle(null)} destructive={toggle.isActive}
          title={toggle.isActive ? `Deactivate ${toggle.firstName} ${toggle.lastName}?` : `Reactivate ${toggle.firstName}?`}
          description={toggle.isActive ? "They are signed out within a minute and can no longer sign in. Their history is kept." : "They will be able to sign in again with their current password."}
          confirmLabel={toggle.isActive ? "Deactivate" : "Reactivate"} pending={setActive.isPending} onConfirm={() => setActive.mutate(toggle)} />
      )}
      {dialog?.kind === "new" && <NewEmployeeDialog onClose={() => setDialog(null)} />}
      {dialog?.kind === "edit" && <EditEmployeeDialog user={dialog.user} onClose={() => setDialog(null)} />}
      {dialog?.kind === "roles" && <RolesDialog user={dialog.user} onClose={() => setDialog(null)} />}
      {dialog?.kind === "reset" && <ResetPasswordDialog user={dialog.user} onClose={() => setDialog(null)} />}
    </>
  )
}

function useRoles() {
  return useQuery({ queryKey: qk.roles, queryFn: () => api<Role[]>("/api/roles"), staleTime: 300_000 })
}

function RolePicker({ value, onChange }: { value: string[]; onChange: (v: string[]) => void }) {
  const { data: roles } = useRoles()
  const { user } = useAuth()
  const mine = new Set(user?.permissions ?? [])
  const isSuper = user?.roles.includes("Super Admin")
  return (
    <fieldset className="grid gap-2">
      <legend className="mb-1 text-xs font-medium">Roles</legend>
      {roles?.map((r) => {
        const grantable = r.name === "Super Admin" ? isSuper : r.permissions.every((p) => mine.has(p))
        return (
          <label key={r.name} className="flex items-start gap-2.5 rounded-md border border-hairline p-2.5 text-sm has-[:checked]:border-petal has-[:checked]:bg-blush/50">
            <Checkbox checked={value.includes(r.name)} disabled={!grantable} className="mt-0.5"
              onCheckedChange={(c) => onChange(c ? [...value, r.name] : value.filter((v) => v !== r.name))} />
            <span><span className="font-medium">{r.name}</span><span className="block text-xs text-muted-foreground">{r.description}</span></span>
          </label>
        )
      })}
    </fieldset>
  )
}

function useSaveUser(onClose: () => void, success: string) {
  const queryClient = useQueryClient()
  return {
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["identity"] }); toast.success(success); onClose() },
    onError: (e: unknown) => toast.error(errorMessage(e)),
  }
}

function NewEmployeeDialog({ onClose }: { onClose: () => void }) {
  const [first, setFirst] = useState("")
  const [last, setLast] = useState("")
  const [email, setEmail] = useState("")
  const [roles, setRoles] = useState<string[]>(["Sales Employee"])
  const [password] = useState(generatePassword)
  const [created, setCreated] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const handlers = useSaveUser(() => undefined, "Employee created")
  const save = useMutation({
    mutationFn: () => api<UserSummary>("/api/users", { method: "POST", body: { email: email.trim(), firstName: first.trim(), lastName: last.trim(), temporaryPassword: password, roles } }),
    onSuccess: async (u) => { await handlers.onSuccess(); setCreated(u.email) },
    onError: (e) => {
      if (e instanceof ApiError && e.code === "users.duplicate_email") setFieldErrors({ email: e.message })
      else if (e instanceof ApiError && Object.keys(e.fieldErrors).length) setFieldErrors(Object.fromEntries(Object.entries(e.fieldErrors).map(([k, v]) => [k, v[0]])))
      else toast.error(errorMessage(e))
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-lg">
        {created ? (
          <>
            <DialogHeader>
              <DialogTitle>Account created</DialogTitle>
              <DialogDescription>Give these details to the employee privately. They must choose their own password when they first sign in.</DialogDescription>
            </DialogHeader>
            <div className="grid gap-2 rounded-md border border-petal bg-blush/60 p-4 text-sm">
              <div><span className="text-muted-foreground">Email:</span> <span className="code">{created}</span></div>
              <div><span className="text-muted-foreground">Temporary password:</span> <span className="code select-all">{password}</span></div>
            </div>
            <p className="text-xs text-muted-foreground">This password is shown only once and is not stored anywhere readable.</p>
            <DialogFooter>
              <Button variant="outline" onClick={() => navigator.clipboard.writeText(`${created}\n${password}`).then(() => toast.success("Copied"))}>Copy</Button>
              <Button onClick={onClose}>Done</Button>
            </DialogFooter>
          </>
        ) : (
          <>
            <DialogHeader><DialogTitle>Add employee</DialogTitle></DialogHeader>
            <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); setFieldErrors({}); if (first.trim() && last.trim() && email.trim() && roles.length) save.mutate() }}>
              <div className="grid grid-cols-2 gap-4">
                <Field label="First name" htmlFor="nu-first" error={fieldErrors.firstName} required><Input id="nu-first" value={first} onChange={(e) => setFirst(e.target.value)} /></Field>
                <Field label="Last name" htmlFor="nu-last" error={fieldErrors.lastName} required><Input id="nu-last" value={last} onChange={(e) => setLast(e.target.value)} /></Field>
              </div>
              <Field label="Work email" htmlFor="nu-email" error={fieldErrors.email} required><Input id="nu-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
              <RolePicker value={roles} onChange={setRoles} />
              <p className="text-xs text-muted-foreground">A temporary password is generated; you'll see it once after saving.</p>
              <DialogFooter>
                <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
                <SubmitButton pending={save.isPending} disabled={!first.trim() || !last.trim() || !email.trim() || roles.length === 0}>Create account</SubmitButton>
              </DialogFooter>
            </form>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

function EditEmployeeDialog({ user, onClose }: { user: UserSummary; onClose: () => void }) {
  const [first, setFirst] = useState(user.firstName)
  const [last, setLast] = useState(user.lastName)
  const [email, setEmail] = useState(user.email)
  const save = useMutation({
    mutationFn: () => api(`/api/users/${user.id}`, { method: "PUT", body: { email: email.trim(), firstName: first.trim(), lastName: last.trim() } }),
    ...useSaveUser(onClose, "Employee updated"),
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>Edit {user.firstName} {user.lastName}</DialogTitle></DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
          <div className="grid grid-cols-2 gap-4">
            <Field label="First name" htmlFor="eu-first" required><Input id="eu-first" value={first} onChange={(e) => setFirst(e.target.value)} /></Field>
            <Field label="Last name" htmlFor="eu-last" required><Input id="eu-last" value={last} onChange={(e) => setLast(e.target.value)} /></Field>
          </div>
          <Field label="Email (sign-in)" htmlFor="eu-email" required><Input id="eu-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} disabled={!first.trim() || !last.trim() || !email.trim()}>Save</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function RolesDialog({ user, onClose }: { user: UserSummary; onClose: () => void }) {
  const [roles, setRoles] = useState(user.roles)
  const save = useMutation({ mutationFn: () => api(`/api/users/${user.id}/roles`, { method: "PUT", body: { roles } }), ...useSaveUser(onClose, "Roles updated") })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Roles for {user.firstName} {user.lastName}</DialogTitle>
          <DialogDescription>Changes apply to their open session within a minute. Roles with permissions you don't hold can't be granted.</DialogDescription>
        </DialogHeader>
        <RolePicker value={roles} onChange={setRoles} />
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button disabled={roles.length === 0 || save.isPending} onClick={() => save.mutate()}>Save roles</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ResetPasswordDialog({ user, onClose }: { user: UserSummary; onClose: () => void }) {
  const [password, setPassword] = useState(generatePassword)
  const [done, setDone] = useState(false)
  const valid = passwordRule.safeParse(password).success
  const save = useMutation({
    mutationFn: () => api(`/api/users/${user.id}/reset-password`, { method: "POST", body: { temporaryPassword: password } }),
    onSuccess: () => setDone(true), onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Reset password for {user.firstName}</DialogTitle>
          <DialogDescription>{done ? "Share it privately. They will be asked to choose a new one at sign-in." : "Their sessions end and the account is unlocked. They must change this temporary password at next sign-in."}</DialogDescription>
        </DialogHeader>
        {done ? (
          <div className="rounded-md border border-petal bg-blush/60 p-4 text-sm"><span className="text-muted-foreground">Temporary password:</span> <span className="code select-all">{password}</span></div>
        ) : (
          <Field label="Temporary password" htmlFor="rp-pass" error={!valid ? "At least 10 characters with upper, lower and a digit." : undefined}>
            <Input id="rp-pass" className="font-mono" value={password} onChange={(e) => setPassword(e.target.value)} />
          </Field>
        )}
        <DialogFooter>
          {done ? <Button onClick={onClose}>Done</Button> : <>
            <Button variant="ghost" onClick={onClose}>Cancel</Button>
            <Button disabled={!valid || save.isPending} onClick={() => save.mutate()}>Reset password</Button>
          </>}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
