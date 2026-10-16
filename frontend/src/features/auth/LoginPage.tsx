import { useState } from "react"
import { Navigate, useLocation, useNavigate } from "react-router"
import { useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import wordmark from "@/assets/wordmark.png"
import { useAuth } from "@/auth/auth"
import { Field, SubmitButton } from "@/components/common/controls"
import { Input } from "@/components/ui/input"
import { ApiError, errorMessage } from "@/lib/api"

const schema = z.object({
  email: z.string().trim().min(1, "Enter your username."),
  password: z.string().min(1, "Enter your password."),
})
type Values = z.infer<typeof schema>

export function LoginPage() {
  const { user, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const from = (location.state as { from?: string } | null)?.from ?? "/"
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<Values>({ resolver: zodResolver(schema) })

  if (user) return <Navigate to={from} replace />

  const onSubmit = handleSubmit(async ({ email, password }) => {
    setError(null)
    try {
      const me = await login(email, password)
      const isSuperAdmin = me.roles.includes("Super Admin")
      navigate(me.mustChangePassword ? "/profile" : isSuperAdmin ? from : "/inventory", { replace: true })
    } catch (e) {
      setError(e instanceof ApiError && e.status === 401 ? e.message : errorMessage(e))
    }
  })

  return (
    <div className="grid min-h-svh lg:grid-cols-[1.1fr_1fr]">
      <div className="relative hidden overflow-hidden bg-blush lg:block">
        <img src={wordmark} alt="" className="absolute top-1/2 left-1/2 w-[70%] max-w-[560px] -translate-x-1/2 -translate-y-1/2 mix-blend-multiply" />
        <p className="absolute bottom-8 left-10 max-w-xs text-sm text-rose-ink/80">
          Orders, stock and deliveries for ÀLara Line — in one place.
        </p>
      </div>

      <div className="flex items-center justify-center px-6 py-12">
        <form onSubmit={onSubmit} noValidate className="w-full max-w-sm">
          <img src={wordmark} alt="ÀLara Line" className="mb-8 h-14 w-auto object-contain lg:hidden" />
          <p className="eyebrow">Operations</p>
          <h1 className="font-display mt-1 text-3xl font-medium">Sign in</h1>
          <p className="mt-1 text-sm text-muted-foreground">Use the account your administrator created for you.</p>

          <div className="mt-8 grid gap-4">
            <Field label="Username" htmlFor="email" error={errors.email?.message}>
              <Input id="email" type="text" autoComplete="username" autoFocus aria-invalid={!!errors.email} {...register("email")} />
            </Field>
            <Field label="Password" htmlFor="password" error={errors.password?.message}>
              <Input id="password" type="password" autoComplete="current-password" aria-invalid={!!errors.password} {...register("password")} />
            </Field>
            {error && <p role="alert" className="rounded-md bg-tone-red-bg px-3 py-2 text-sm text-tone-red">{error}</p>}
            <SubmitButton pending={isSubmitting} size="lg" className="mt-2 w-full">Sign in</SubmitButton>
          </div>
          <p className="mt-8 text-xs text-muted-foreground">Forgot your password? Ask an administrator to reset it.</p>
        </form>
      </div>
    </div>
  )
}
