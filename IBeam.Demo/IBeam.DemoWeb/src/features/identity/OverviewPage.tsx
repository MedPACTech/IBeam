import { ArrowRight, Database, KeyRound, Route, ShieldCheck } from "lucide-react";
import { CodeBlock } from "../../components/CodeBlock";
import { TokenInspector } from "../../components/TokenInspector";
import { identitySnippets } from "../../snippets/identitySnippets";

const tracks = [
  {
    icon: KeyRound,
    title: "Auth Workflows",
    text: "Register, OTP login, password login, two-factor setup, OAuth, credential linking, refresh, and revocation."
  },
  {
    icon: ShieldCheck,
    title: "Tenant Access",
    text: "Show tenant provisioning modes, role grants, permission mappings, and authorization attributes in action."
  },
  {
    icon: Database,
    title: "Persistence",
    text: "Connect each workflow to Azure Table schema, records, table prefixes, and local Azurite setup."
  }
];

export function OverviewPage() {
  return (
    <div className="page-grid">
      <section className="hero-panel">
        <div className="eyebrow">
          <Route size={16} />
          Identity walkthrough
        </div>
        <h1>Explore IBeam Identity as a working developer console.</h1>
        <p>
          Run a workflow, inspect the request and response, decode the token, then copy the service,
          configuration, and API snippets behind it.
        </p>
        <div className="flow-strip" aria-label="Identity architecture flow">
          <span>React demo</span>
          <ArrowRight size={15} />
          <span>Demo API</span>
          <ArrowRight size={15} />
          <span>IBeam.Identity.Api</span>
          <ArrowRight size={15} />
          <span>Azure Tables</span>
        </div>
      </section>

      <section className="track-grid">
        {tracks.map((track) => {
          const Icon = track.icon;
          return (
            <article className="track-card" key={track.title}>
              <Icon size={22} />
              <h2>{track.title}</h2>
              <p>{track.text}</p>
            </article>
          );
        })}
      </section>

      <section className="panel">
        <div className="panel-heading">
          <h2>First Milestone</h2>
        </div>
        <ol className="milestone-list">
          <li>Wire the demo API to `AddIBeamIdentityApi(...)` and map the reusable controllers.</li>
          <li>Run Azurite with `UseDevelopmentStorage=true` for the identity repository.</li>
          <li>Complete registration or OTP login, then inspect the issued token and table changes.</li>
          <li>Create a role, grant it to the user, map a permission, and prove authorization behavior.</li>
        </ol>
      </section>

      <aside className="side-stack">
        <CodeBlock title="Host Composition" code={identitySnippets.apiComposition} />
        <TokenInspector />
      </aside>
    </div>
  );
}
