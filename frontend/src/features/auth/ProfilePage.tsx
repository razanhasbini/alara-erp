import { useLocation } from "react-router"
import { useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { Field, SubmitButton } from "@/components/common/controls"
import { Facts, PageHeader, Section } from "@/components/common/page"
import { Input } from "@/components/ui/input"
import { api, ApiError, errorMessage } from "@/lib/api"
import { passwordRule } from "@/lib/rules"

const schema = z.object({
  currentPassword: z.string().min(1, "Enter your current password."),
  newPassword: passwordRule,
  confirm: z.string(),
}).refine((v) => v.newPassword === v.confirm, { path: ["confirm"], message: "Passwords do not match." })
  .refine((v) => v.newPassword !== v.currentPassword, { path: ["newPassword"], message: "Choose a different password." })
type Values = z.infer<typeof schema>

export function ProfilePage() {
  const { user, refresh } = useAuth()
  const forced = (useLocation().state as { forced?: boolean } | null)?.forced || user?.mustChangePassword
  const { register, handleSubmit, reset, setError, formState: { errors, isSubmitting } } = useForm<Values>({ resolver: zodResolver(schema) })

  const onSubmit = handleSubmit(async (v) => {
    try {
      await api("/api/auth/change-password", { method: "POST", body: { currentPassword: v.currentPassword, newPassword: v.newPassword } })
      toast.success("Password changed")
      reset()
      await refresh()
    } catch (e) {
      if (e instanceof ApiError && e.fieldErrors.currentPassword) setError("currentPassword", { message: e.fieldErrors.currentPassword[0] })
      else if (e instanceof ApiError && e.fieldErrors.newPassword) setError("newPassword", { message: e.fieldErrors.newPassword[0] })
      else toast.error(errorMessage(e))
    }
  })

  if (!user) return null
  return (
    <>
      <PageHeader title="Profile" description="Your account and sign-in details." />
      {forced && (
        <p role="alert" className="mb-6 rounded-md border border-petal bg-blush px-4 py-3 text-sm">
          You signed in with a temporary password. Choose your own password to continue.
        </p>
      )}
      <div className="grid gap-6 lg:grid-cols-2">
        <Section title="Account" bodyClassName="p-4">
          <Facts items={[
            ["Name", `${user.firstName} ${user.lastName}`],
            ["Email", user.email],
          ]} />
        </Section>
        <Section title="Change password" bodyClassName="p-4">
          <form id="password" onSubmit={onSubmit} noValidate className="grid gap-4">
            <Field label="Current password" htmlFor="currentPassword" error={errors.currentPassword?.message} required>
              <Input id="currentPassword" type="password" autoComplete="current-password" {...register("currentPassword")} />
            </Field>
            <Field label="New password" htmlFor="newPassword" error={errors.newPassword?.message}
              hint="At least 10 characters with upper- and lowercase letters and a digit." required>
              <Input id="newPassword" type="password" autoComplete="new-password" {...register("newPassword")} />
            </Field>
            <Field label="Repeat new password" htmlFor="confirm" error={errors.confirm?.message} required>
              <Input id="confirm" type="password" autoComplete="new-password" {...register("confirm")} />
            </Field>
            <div><SubmitButton pending={isSubmitting}>Change password</SubmitButton></div>
          </form>
        </Section>
      </div>
    </>
  )
}
