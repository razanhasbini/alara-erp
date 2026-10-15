import { z } from "zod"
import type { OrderSource } from "@/types/api"

/** Mirrors the server's Identity password policy so errors show before submitting. */
export const passwordRule = z.string()
  .min(10, "At least 10 characters.")
  .regex(/[a-z]/, "Add a lowercase letter.")
  .regex(/[A-Z]/, "Add an uppercase letter.")
  .regex(/[0-9]/, "Add a digit.")

export const SOURCES: OrderSource[] = ["WhatsApp", "Instagram", "Website", "Other"]
