import {
  CalendarCheck,
  House,
  LayoutGrid,
  ListChecks,
  type LucideIcon,
  Settings2,
  Users,
} from "lucide-react";
import { ReactNode } from "react";
import { type Access, commishHome, hasCommish, type Route } from "./routes";
import { SwanMark } from "./Swan";

type Destination = {
  label: string;
  path: string;
  icon: LucideIcon;
  current: boolean;
};

// Only destinations that exist are listed, so nothing in the navigation leads nowhere.
function destinations(access: Access, route: Route): Destination[] {
  if (route.mode === "coach")
    return [
      {
        label: "Home",
        path: "/",
        icon: House,
        current: route.page === "home",
      },
      {
        label: "Picks",
        path: "/picks",
        icon: ListChecks,
        current: route.page === "picks",
      },
      {
        label: "Draft",
        path: "/roster",
        icon: LayoutGrid,
        current: route.page === "draft",
      },
    ];
  return [
    ...(access.isAdmin
      ? [
          {
            label: "Today",
            path: "/commish",
            icon: CalendarCheck,
            // A week is opened from Today, so Today stays marked while one is open.
            current: route.page === "today" || route.page === "week",
          },
        ]
      : []),
    ...(access.canDraft
      ? [
          {
            label: "Draft",
            path: "/commish/draft",
            icon: Users,
            current: route.page === "draft",
          },
        ]
      : []),
    ...(access.canSetup
      ? [
          {
            label: "Season setup",
            path: "/commish/setup",
            icon: Settings2,
            current: route.page === "setup",
          },
        ]
      : []),
  ];
}

export default function Shell({
  name,
  role,
  access,
  route,
  onNavigate,
  onSignOut,
  children,
}: {
  name: string;
  role: "Admin" | "Coach";
  access: Access;
  route: Route;
  onNavigate: (to: string) => void;
  onSignOut: () => void;
  children: ReactNode;
}) {
  const go = (to: string) => (event: React.MouseEvent) => {
    event.preventDefault();
    onNavigate(to);
  };
  const modes = hasCommish(access)
    ? [
        { mode: "coach", label: "Coach", path: "/" },
        { mode: "commish", label: "Commish", path: commishHome(access) },
      ]
    : [];

  return (
    <div className="shell" data-mode={route.mode}>
      <a className="skip" href="#content">
        Skip to main content
      </a>
      <header className="shell-top">
        <span className="mark">
          <SwanMark />
          Swan League
        </span>
        {modes.length > 0 && (
          <div className="mode-switch" role="group" aria-label="Mode">
            {modes.map((item) => (
              <a
                key={item.mode}
                href={item.path}
                aria-current={route.mode === item.mode ? "page" : undefined}
                onClick={go(item.path)}
              >
                {item.label}
              </a>
            ))}
          </div>
        )}
        <p className="who">
          Signed in as {name} · {role}
        </p>
        <button type="button" className="quiet" onClick={onSignOut}>
          Sign out
        </button>
      </header>
      <nav className="shell-nav" aria-label="Primary">
        <ul>
          {destinations(access, route).map((item) => (
            <li key={item.path}>
              <a
                href={item.path}
                aria-current={item.current ? "page" : undefined}
                onClick={go(item.path)}
              >
                <item.icon aria-hidden="true" size={20} strokeWidth={2} />
                {item.label}
              </a>
            </li>
          ))}
        </ul>
      </nav>
      <main id="content" tabIndex={-1} className="shell-main">
        {children}
      </main>
    </div>
  );
}
