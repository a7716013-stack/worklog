export default {
  async fetch(request, env) {
    const deny = (status, text) => new Response(text, {status, headers: {"Cache-Control": "no-store"}});
    if (!env.ORIGIN_KEY) return deny(503, "Website setup is in progress.");
    const publicUrl = new URL(request.url);
    const target = new URL(env.ORIGIN);
    target.pathname = publicUrl.pathname;
    target.search = publicUrl.search;
    const headers = new Headers(request.headers);
    for (const key of [...headers.keys()]) {
      if (key.startsWith("x-forwarded-") || key === "forwarded" || key === "x-azure-fdid" || key === "host") headers.delete(key);
    }
    headers.set("x-azure-fdid", env.ORIGIN_KEY.trim());
    const upstream = await fetch(target, {
      method: request.method, headers,
      body: ["GET", "HEAD"].includes(request.method) ? undefined : request.body,
      redirect: "manual", cache: "no-store"
    });
    const result = new Response(upstream.body, upstream);
    result.headers.set("Cache-Control", "no-store");
    const location = result.headers.get("Location");
    if (location) {
      const redirect = new URL(location, target);
      if (redirect.origin === target.origin) {
        redirect.host = publicUrl.host; redirect.protocol = publicUrl.protocol;
        result.headers.set("Location", redirect.href);
      }
    }
    const cookies = upstream.headers.getSetCookie();
    result.headers.delete("Set-Cookie");
    for (const cookie of cookies) result.headers.append("Set-Cookie", cookie.replace(/;\s*Domain=[^;]+/ig, ""));
    return result;
  }
};
