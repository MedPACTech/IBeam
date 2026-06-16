import { Activity, CircleAlert, CircleCheck, Copy, ServerCog } from "lucide-react";
import type { PropsWithChildren } from "react";
import { NavLink } from "react-router-dom";
import type { DemoRoute } from "../app/routes";

type AppShellProps = PropsWithChildren<{
  routes: DemoRoute[];
}>;

export function AppShell({ children, routes }: AppShellProps) {
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark">IB</div>
          <div>
            <div className="brand-title">IBeam Identity</div>
            <div className="brand-subtitle">Developer demo</div>
          </div>
        </div>

        <nav className="nav-list" aria-label="Identity demo navigation">
          {routes.map((route) => {
            const Icon = route.icon;
            return (
              <NavLink
                key={route.path}
                to={route.path}
                end={route.path === "/"}
                className={({ isActive }) => `nav-link${isActive ? " is-active" : ""}`}
              >
                <Icon size={17} />
                <span>{route.label}</span>
              </NavLink>
            );
          })}
        </nav>
      </aside>

      <div className="workspace">
        <header className="topbar">
          <div className="status-pill">
            <ServerCog size={16} />
            <span>API proxy</span>
            <strong>/api</strong>
          </div>
          <div className="status-pill muted">
            <Activity size={16} />
            <span>Token</span>
            <strong>none</strong>
          </div>
          <div className="status-pill muted">
            <CircleAlert size={16} />
            <span>Tenant</span>
            <strong>unselected</strong>
          </div>
          <button className="icon-button" type="button" title="Copy current context">
            <Copy size={16} />
          </button>
          <div className="status-pill ok">
            <CircleCheck size={16} />
            <span>Console ready</span>
          </div>
        </header>

        <main className="content">{children}</main>
      </div>
    </div>
  );
}
