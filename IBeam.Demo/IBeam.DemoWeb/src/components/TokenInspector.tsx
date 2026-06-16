import { KeyRound } from "lucide-react";
import { useMemo, useState } from "react";

export function TokenInspector() {
  const [token, setToken] = useState("");

  const decoded = useMemo(() => {
    const parts = token.split(".");
    if (parts.length !== 3) {
      return null;
    }

    try {
      const payload = parts[1].replace(/-/g, "+").replace(/_/g, "/");
      const json = decodeURIComponent(
        atob(payload)
          .split("")
          .map((char) => `%${`00${char.charCodeAt(0).toString(16)}`.slice(-2)}`)
          .join("")
      );
      return JSON.parse(json);
    } catch {
      return null;
    }
  }, [token]);

  return (
    <section className="token-inspector">
      <div className="panel-heading">
        <h3>
          <KeyRound size={16} />
          Token Inspector
        </h3>
      </div>
      <textarea
        value={token}
        onChange={(event) => setToken(event.target.value)}
        placeholder="Paste a JWT access token"
        rows={5}
      />
      <pre>{decoded ? JSON.stringify(decoded, null, 2) : "Paste a JWT to inspect claims."}</pre>
    </section>
  );
}
