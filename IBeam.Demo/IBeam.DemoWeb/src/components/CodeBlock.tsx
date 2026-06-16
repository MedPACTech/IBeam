import { Copy } from "lucide-react";

type CodeBlockProps = {
  title: string;
  code: string;
};

export function CodeBlock({ title, code }: CodeBlockProps) {
  async function copyCode() {
    await navigator.clipboard.writeText(code);
  }

  return (
    <section className="code-block">
      <div className="panel-heading">
        <h3>{title}</h3>
        <button className="icon-button" type="button" title="Copy snippet" onClick={copyCode}>
          <Copy size={15} />
        </button>
      </div>
      <pre>
        <code>{code}</code>
      </pre>
    </section>
  );
}
