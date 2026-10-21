// Where each address leads, for whoever is signed in. One app has two modes: every person is a coach, and an
// administrator or a season's commissioner also has the Commish area under /commish.

export type Mode = "coach" | "commish";

export type Access = {
  isAdmin: boolean;
  // Season setup: an administrator, or the commissioner of the season in Setup.
  canSetup: boolean;
  // The commissioner draft view: an administrator, or the commissioner of the live season.
  canDraft: boolean;
};

export type Route =
  | { mode: "coach"; page: "home" | "picks" | "draft" }
  | { mode: "commish"; page: "today" | "draft" | "setup" }
  | { mode: "commish"; page: "week"; weekId: string };

const weekPath = /^\/(?:commish\/)?weeks\/([0-9a-f-]{36})\/?$/i;

export const hasCommish = (access: Access) =>
  access.isAdmin || access.canSetup || access.canDraft;

// The first Commish page this person may open: Today for an administrator, otherwise what their commission covers.
export function commishHome(access: Access) {
  if (access.isAdmin) return "/commish";
  if (access.canDraft) return "/commish/draft";
  if (access.canSetup) return "/commish/setup";
  return "/";
}

// Addresses from before the Commish area existed keep working and move to their new place.
export function canonical(path: string) {
  if (path === "/setup" || path.startsWith("/setup/")) return `/commish${path}`;
  if (path === "/draft" || path === "/draft/") return "/commish/draft";
  const week = weekPath.exec(path);
  if (week && !path.startsWith("/commish/")) return `/commish/weeks/${week[1]}`;
  return path;
}

// The page an address shows and the address it should show it under. A page this person may not open falls back
// to their Commish home, or to their own picks when they have no Commish area at all.
export function resolve(
  rawPath: string,
  access: Access,
): { route: Route; path: string } {
  const path = canonical(rawPath);
  const fallback = () =>
    hasCommish(access)
      ? resolveCommishHome(access)
      : { route: { mode: "coach", page: "home" } as Route, path: "/" };

  if (path === "/picks" || path === "/picks/")
    return { route: { mode: "coach", page: "picks" }, path: "/picks" };

  if (path === "/roster" || path === "/roster/")
    return { route: { mode: "coach", page: "draft" }, path: "/roster" };

  if (path === "/commish" || path === "/commish/")
    return access.isAdmin
      ? { route: { mode: "commish", page: "today" }, path: "/commish" }
      : fallback();

  if (path === "/commish/draft" || path === "/commish/draft/")
    return access.canDraft
      ? { route: { mode: "commish", page: "draft" }, path: "/commish/draft" }
      : fallback();

  if (path === "/commish/setup" || path.startsWith("/commish/setup/"))
    return access.canSetup
      ? { route: { mode: "commish", page: "setup" }, path }
      : fallback();

  const week = weekPath.exec(path);
  if (week)
    return access.isAdmin
      ? {
          route: { mode: "commish", page: "week", weekId: week[1] },
          path,
        }
      : fallback();

  if (path.startsWith("/commish")) return fallback();
  return { route: { mode: "coach", page: "home" }, path: rawPath };
}

function resolveCommishHome(access: Access) {
  return resolve(commishHome(access), access);
}

// The mode last used on this device. Storage can be missing or refuse access; then nothing is remembered.
const modeKey = "swan.mode";

export function rememberedMode(): Mode | null {
  try {
    const value = window.localStorage.getItem(modeKey);
    return value === "commish" || value === "coach" ? value : null;
  } catch {
    return null;
  }
}

export function rememberMode(mode: Mode) {
  try {
    window.localStorage.setItem(modeKey, mode);
  } catch {
    // nothing to do: the mode is simply not remembered
  }
}
