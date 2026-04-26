---
name: "sunnynet-mcp"
description: "Use when working with the SunnyNet WPF MCP server to inspect captured HTTP/WebSocket/TCP/UDP sessions, query request or response bodies, manage favorites and notes, search/highlight traffic, control proxy/capture state, or configure SunnyNet rules from an AI session."
---

# SunnyNet MCP

Use this skill when a user asks the agent to operate SunnyNet through MCP: view captured requests, analyze a session, inspect response content, search traffic, manage favorites/notes, use WebSocket data, or control SunnyNet proxy/capture settings.

## Preconditions

- SunnyNet WPF must be running.
- MCP is not auto-started by default; the user must enable it from the bottom status bar or settings page.
- The MCP bridge is expected at the app runtime path `mcp\sunnynet-mcp.exe`.
- Tool namespace is usually `mcp__sunnynet__`.

If SunnyNet MCP tools are unavailable, tell the user to enable MCP in SunnyNet and check their client MCP config before trying again.

## Core Workflow

1. For traffic questions, start with `request_list` or `request_search`.
2. Use the UI-visible `index` when the user says “序号 5 / #5”; use `theology` only when you already have the backend ID.
3. For one session, call `request_get` first, then call `request_get_response_body_decoded` only when body content is needed.
4. For WebSocket/TCP/UDP, call `connection_list`, then `socket_data_list`, then `socket_data_get` or `socket_data_get_range`.
5. Prefer summaries for large bodies; do not paste huge responses unless the user explicitly asks.
6. Do not mutate or destroy data unless the user explicitly asks.

## Safety Rules

Ask for explicit confirmation before:

- `request_clear`, `request_delete`
- `proxy_stop`, `proxy_set_ie`, `proxy_unset_ie`, `proxy_set_port`
- `request_modify_body`, `response_modify_body`
- `request_modify_header`, `response_modify_header`
- `request_resend`, `request_block`, `request_release_all`
- `breakpoint_clear`, `replace_rules_clear`

Favorites and notes are low-risk, but still follow the user's exact target session numbers.

## Common Tasks

### List current sessions

Use `request_list` with a small limit first:

```json
{ "limit": 20, "offset": 0 }
```

Return the important fields: `index`, `method`, `url`, `statusCode`, `length`, `notes`, `isFavorite`.

### Analyze one session

1. `request_get` with `{ "index": 5 }`
2. If needed, `request_get_response_body_decoded` with `{ "index": 5 }`
3. Summarize request URL, method, headers, parameters, status, content type, and response meaning.

### Work with favorites and notes

- List favorites: `request_favorites_list`
- Add favorite: `request_favorite_add` with `index` or `theology`
- Remove favorite: `request_favorite_remove` with `index` or `theology`
- Set or clear notes: `request_notes_set`; pass empty `notes` to clear.

### Search and highlight traffic

- Search in URL/body: `request_body_search`
- Search only favorites: add `favoritesOnly: true`
- Highlight in UI: `request_highlight_search`
- Clear highlight: `request_highlight_clear`

### Breakpoints and intercept rules

Use `breakpoint_add` for URL regex breakpoints, then `breakpoint_list` to verify.

For intercepted sessions, inspect the current session before modifying or releasing. If the user wants to edit upstream/downstream data, prefer body/header modify tools only after they clearly specify the change.

## Detailed Tool Reference

For the complete tool list and parameters, read `references/tools.md`.

