import { createContext } from "react";

// Creating or starting a season changes what the signed-in person may reach (Season setup, the roster draft), so the pages that do it call this to have the app read /api/me again.
export const RefreshAccess = createContext<() => Promise<void>>(async () => {});
