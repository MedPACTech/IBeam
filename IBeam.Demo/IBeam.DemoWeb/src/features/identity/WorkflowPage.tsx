import { ClipboardList, ListChecks } from "lucide-react";
import { CodeBlock } from "../../components/CodeBlock";
import { RequestConsole } from "../../components/RequestConsole";
import { TokenInspector } from "../../components/TokenInspector";
import { identitySnippets, type SnippetKey } from "../../snippets/identitySnippets";

type WorkflowPageProps = {
  title: string;
  summary: string;
  endpoints: string[];
  snippetKey: SnippetKey;
};

export function WorkflowPage({ title, summary, endpoints, snippetKey }: WorkflowPageProps) {
  const firstEndpoint = endpoints.find((endpoint) => /^(GET|POST|PUT|DELETE)\s/.test(endpoint));

  return (
    <div className="page-grid">
      <section className="hero-panel compact">
        <div className="eyebrow">
          <ClipboardList size={16} />
          Identity module
        </div>
        <h1>{title}</h1>
        <p>{summary}</p>
      </section>

      <section className="panel">
        <div className="panel-heading">
          <h2>
            <ListChecks size={18} />
            Surface Area
          </h2>
        </div>
        <div className="endpoint-list">
          {endpoints.map((endpoint) => (
            <code key={endpoint}>{endpoint}</code>
          ))}
        </div>
      </section>

      {firstEndpoint ? (
        <RequestConsole endpoint={firstEndpoint} defaultBody={getDefaultBody(snippetKey)} />
      ) : (
        <section className="panel">
          <div className="panel-heading">
            <h2>Interactive Flow</h2>
          </div>
          <p className="muted-copy">
            This section is configuration-led. The next pass can connect it to API health and schema
            inspection endpoints once the demo API exposes them.
          </p>
        </section>
      )}

      <aside className="side-stack">
        <CodeBlock title="Starter Snippet" code={identitySnippets[snippetKey]} />
        <TokenInspector />
      </aside>
    </div>
  );
}

function getDefaultBody(snippetKey: SnippetKey): unknown {
  switch (snippetKey) {
    case "registration":
      return {
        email: "developer@example.com",
        displayName: "Demo Developer",
        resetUrlBase: "http://localhost:5174/registration"
      };
    case "otp":
      return {
        destination: "16145551212",
        tenantId: null
      };
    case "passwordLogin":
      return {
        email: "developer@example.com",
        password: "correct horse battery staple"
      };
    case "twoFactor":
      return {
        method: "email"
      };
    case "roles":
      return {
        name: "admin"
      };
    case "permissions":
      return {
        permissionName: "Identity.ManageRoles",
        roleNames: ["owner", "admin"],
        roleIds: []
      };
    default:
      return undefined;
  }
}
