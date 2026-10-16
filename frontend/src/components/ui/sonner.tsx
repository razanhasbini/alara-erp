import { useTheme } from "next-themes"
import { Toaster as Sonner, type ToasterProps } from "sonner"
import { CircleCheckIcon, InfoIcon, TriangleAlertIcon, OctagonXIcon, Loader2Icon } from "lucide-react"

const Toaster = ({ ...props }: ToasterProps) => {
  const { theme = "system" } = useTheme()

  return (
    <Sonner
      theme={theme as ToasterProps["theme"]}
      className="toaster group"
      icons={{
        success: (
          <CircleCheckIcon className="size-4" />
        ),
        info: (
          <InfoIcon className="size-4" />
        ),
        warning: (
          <TriangleAlertIcon className="size-4" />
        ),
        error: (
          <OctagonXIcon className="size-4" />
        ),
        loading: (
          <Loader2Icon className="size-4 animate-spin" />
        ),
      }}
      style={
        {
          "--normal-bg": "var(--popover)",
          "--normal-text": "var(--popover-foreground)",
          "--normal-border": "var(--border)",
          "--border-radius": "var(--radius)",
        } as React.CSSProperties
      }
      toastOptions={{
        duration: 6000,
        classNames: {
          toast: "cn-toast !w-[min(26rem,calc(100vw-2rem))] !gap-3 !rounded-lg !border !px-4 !py-3.5 !shadow-lg",
          title: "!text-sm !font-semibold !leading-5",
          description: "!text-sm !leading-5 !text-current/80",
          icon: "![&_svg]:size-5",
          success: "!border-tone-green/30 !bg-tone-green-bg !text-tone-green",
          warning: "!border-tone-amber/30 !bg-tone-amber-bg !text-tone-amber",
          error: "!border-tone-red/30 !bg-tone-red-bg !text-tone-red",
          info: "!border-tone-blue/30 !bg-tone-blue-bg !text-tone-blue",
        },
      }}
      {...props}
    />
  )
}

export { Toaster }
