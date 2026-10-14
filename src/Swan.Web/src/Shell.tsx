import { ReactNode } from "react";

type Destination = { label: string; path: string; soon?: boolean };

// Destinations that do not exist yet are shown as plain labelled text, not links, so nothing leads nowhere.
function destinations(role: "Admin" | "Coach"): Destination[] {
  return [
    { label: "Home", path: "/" },
    { label: "Week", path: "", soon: true },
    { label: "Standings", path: "", soon: true },
    ...(role === "Admin" ? [{ label: "Season setup", path: "/setup" }] : []),
  ];
}

export default function Shell({
  name,
  role,
  path,
  onNavigate,
  onSignOut,
  children,
}: {
  name: string;
  role: "Admin" | "Coach";
  path: string;
  onNavigate: (to: string) => void;
  onSignOut: () => void;
  children: ReactNode;
}) {
  const inSetup = path === "/setup" || path.startsWith("/setup/");
  return (
    <div className="shell">
      <a className="skip" href="#content">
        Skip to main content
      </a>
      <header className="shell-top">
        <span className="mark">Swan League</span>
        <p className="who">
          Signed in as {name} · {role}
        </p>
        <button type="button" className="quiet" onClick={onSignOut}>
          Sign out
        </button>
      </header>
      <nav className="shell-nav" aria-label="Primary">
        <ul>
          {destinations(role).map((item) => {
            const current =
              item.path === "/setup" ? inSetup : !inSetup && item.path === "/";
            return (
              <li key={item.label}>
                {item.soon ? (
                  <span className="soon">
                    {item.label}
                    <small>Coming soon</small>
                  </span>
                ) : (
                  <a
                    href={item.path}
                    aria-current={current ? "page" : undefined}
                    onClick={(event) => {
                      event.preventDefault();
                      onNavigate(item.path);
                    }}
                  >
                    {item.label}
                  </a>
                )}
              </li>
            );
          })}
        </ul>
      </nav>
      <main id="content" tabIndex={-1} className="shell-main">
        {children}
      </main>
    </div>
  );
}
