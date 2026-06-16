import { SendHorizontal } from "lucide-react";
import { useMemo, useState } from "react";
import { callIdentityApi, type ApiResult, type HttpMethod } from "../api/identityApi";

type RequestConsoleProps = {
  endpoint: string;
  defaultBody?: unknown;
};

export function RequestConsole({ endpoint, defaultBody }: RequestConsoleProps) {
  const [method, setMethod] = useState<HttpMethod>(endpoint.startsWith("GET") ? "GET" : "POST");
  const [path, setPath] = useState(endpoint.replace(/^(GET|POST|PUT|DELETE)\s+/, ""));
  const [token, setToken] = useState("");
  const [bodyText, setBodyText] = useState(defaultBody ? JSON.stringify(defaultBody, null, 2) : "");
  const [result, setResult] = useState<ApiResult | null>(null);
  const [isSending, setIsSending] = useState(false);

  const parsedBody = useMemo(() => {
    if (!bodyText.trim()) {
      return { ok: true, value: undefined };
    }

    try {
      return { ok: true, value: JSON.parse(bodyText) };
    } catch (error) {
      return {
        ok: false,
        value: error instanceof Error ? error.message : "Invalid JSON"
      };
    }
  }, [bodyText]);

  async function sendRequest() {
    if (!parsedBody.ok) {
      setResult({ ok: false, status: 0, data: null, error: String(parsedBody.value) });
      return;
    }

    setIsSending(true);
    const response = await callIdentityApi(path, {
      method,
      token: token.trim() || undefined,
      body: method === "GET" ? undefined : parsedBody.value
    });
    setResult(response);
    setIsSending(false);
  }

  return (
    <section className="request-console">
      <div className="panel-heading">
        <h3>Try The Endpoint</h3>
        <button className="primary-button" type="button" onClick={sendRequest} disabled={isSending}>
          <SendHorizontal size={15} />
          <span>{isSending ? "Sending" : "Send"}</span>
        </button>
      </div>

      <div className="request-grid">
        <label>
          Method
          <select value={method} onChange={(event) => setMethod(event.target.value as HttpMethod)}>
            <option>GET</option>
            <option>POST</option>
            <option>PUT</option>
            <option>DELETE</option>
          </select>
        </label>
        <label>
          Path
          <input value={path} onChange={(event) => setPath(event.target.value)} />
        </label>
      </div>

      <label>
        Bearer token
        <input value={token} onChange={(event) => setToken(event.target.value)} placeholder="Paste access token" />
      </label>

      <label>
        Request body
        <textarea value={bodyText} onChange={(event) => setBodyText(event.target.value)} rows={8} />
      </label>

      <div className="response-panel">
        <div className="response-title">Response</div>
        <pre>{result ? JSON.stringify(result, null, 2) : "No request sent yet."}</pre>
      </div>
    </section>
  );
}
