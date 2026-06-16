export type ApiResult<T = unknown> = {
  ok: boolean;
  status: number;
  data: T | null;
  error: string | null;
};

export type HttpMethod = "GET" | "POST" | "PUT" | "DELETE";

export async function callIdentityApi<T = unknown>(
  path: string,
  options: {
    method?: HttpMethod;
    token?: string;
    body?: unknown;
  } = {}
): Promise<ApiResult<T>> {
  const headers = new Headers();
  headers.set("Accept", "application/json");

  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  if (options.token) {
    headers.set("Authorization", `Bearer ${options.token}`);
  }

  try {
    const response = await fetch(path, {
      method: options.method ?? "GET",
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body)
    });

    const text = await response.text();
    const data = text ? JSON.parse(text) : null;

    return {
      ok: response.ok,
      status: response.status,
      data,
      error: response.ok ? null : getErrorMessage(data, response.status)
    };
  } catch (error) {
    return {
      ok: false,
      status: 0,
      data: null,
      error: error instanceof Error ? error.message : "Unknown request failure"
    };
  }
}

function getErrorMessage(data: unknown, status: number) {
  if (data && typeof data === "object" && "message" in data) {
    return String((data as { message: unknown }).message);
  }

  return `Request failed with HTTP ${status}`;
}
