import { createContext } from "react";

// Creating or starting a season changes what the signed-in person may reach (Season setup, the roster draft), so the pages that do it call this to have the app read /api/me again.
export const RefreshAccess = createContext<() => Promise<void>>(async () => {});

// True only for an administrator on a Development server; the server reports it through /api/me.
export const DevelopmentTools = createContext(false);
