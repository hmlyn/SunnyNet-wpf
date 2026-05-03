# SunnyNet MCP Tool Reference

This is a compact reference for the current SunnyNet WPF MCP implementation. Full project documentation also exists at `docs/MCP工具清单.md`.

## Session Identity

- `index`: UI-visible stable session number. Use this when the user says “序号 / #”.
- `theology`: backend unique session ID. Use this when returned by tools or when bulk APIs require it.
- Single-session tools generally accept either `index` or `theology`.
- Bulk tools like `request_resend` and `request_delete` use `theologies`.

## Traffic List

- `request_list(limit, offset, favoritesOnly)`
- `request_search(url, method, status_code, favoritesOnly, limit, offset)`
- `request_stats()`
- `request_get(index|theology)`
- `request_get_response_body_decoded(index|theology)`
- `request_body_search(keyword, scope, favoritesOnly, limit)`
- `request_highlight_search(keyword, type, color)`
- `request_highlight_clear()`

`scope`: `url`, `request_body`, `response_body`, `all`.

`request_get` returns `tlsFingerprint` for HTTPS sessions with captured TLS ClientHello data. It is `null` when no TLS fingerprint exists. Key fields:

- `ja3Hash`, `ja3Text`, `ja3nHash`, `ja3nText`
- `ja4`, `ja4o`, `ja4r`, `ja4ro`
- `sni`, `alpn`, `legacyVersion`, `legacyVersionText`, `highestVersion`, `highestVersionText`
- `cipherSuites`, `extensions`, `supportedGroups`, `ecPointFormats`, `signatureAlgorithms`
- `rawClientHelloHex`

## Favorites and Notes

- `request_favorites_list(limit, offset)`
- `request_favorite_add(index|theology)`
- `request_favorite_remove(index|theology)`
- `request_notes_set(index|theology, notes)`
- `request_tag_set(index|theology|indexes|theologies, color)`
- `request_tag_clear(index|theology|indexes|theologies)`

Empty `notes` clears the note.

`color` accepts normal WPF color strings such as `#FF6A00`; `__strike__` marks a strikethrough row.

## Session Mutation

- `request_modify_header(index|theology, key, value)`
- `request_modify_body(index|theology, body)`
- `response_modify_header(index|theology, key, value)`
- `response_modify_body(index|theology, body)`
- `request_resend(theologies)`
- `request_block(index|theology)`
- `request_release_all()`
- `request_delete(theologies)`
- `request_clear()`
- `request_clear_by_rule(rule)`
- `request_save_all(path)`
- `request_import(path)`

Use these only after the user explicitly asks.

`request_clear_by_rule` supports `all`, `resources`, `no_response`, `failed`, `redirect`, `unfavorite`, `untagged`, `current_filter`.

## Long Connections

- `connection_list(protocol, favoritesOnly, limit, offset)`
- `socket_data_list(index|theology, limit, offset)`
- `socket_data_get(index|theology, index)`
- `socket_data_get_range(index|theology, start, end)`

`protocol`: `websocket`, `tcp`, `udp`.

## Proxy and Capture

- `proxy_get_status()`
- `proxy_start()`
- `proxy_stop()`
- `proxy_set_port(port)`
- `proxy_set_ie()`
- `proxy_unset_ie()`
- `proxy_pause_capture()`
- `proxy_resume_capture()`

## Rules and Settings

- `config_get()`
- `request_rules_list(type)`
- `request_rules_enable(type, hash, enabled)`
- `request_rules_remove(type, hash)`
- `request_rule_hits_list(index|theology, ruleType, limit, offset)`
- `hosts_list()`, `hosts_add(source, target)`, `hosts_remove(index)`
- `replace_rules_list()`, `replace_rules_add(type, source, target)`, `replace_rules_remove(hash|index)`, `replace_rules_clear()`
- `breakpoint_add(url_pattern)`, `breakpoint_list()`, `breakpoint_remove(index)`, `breakpoint_clear()`
- `process_list()`, `process_add_name(name)`, `process_remove_name(name)`
- `cert_install()`, `cert_export(path)`

Rule `type`: `all`, `http_block`, `websocket_block`, `tcp_block`, `udp_block`, `rewrite`, `mapping`.
`replace_rules_*` and `breakpoint_*` are compatibility APIs; prefer `request_rules_*` for the WPF rule center.

Replace rule `type`: `Base64`, `HEX`, `String(UTF8)`, `String(GBK)`, `响应文件`.

## Response Field Hints

Session list/detail results usually include:

- `index`
- `theology`
- `method`
- `url`
- `statusCode`
- `pid`
- `clientIP`
- `sendTime`
- `recTime`
- `way`
- `host`
- `query`
- `tlsFingerprint`
- `length`
- `type`
- `process`
- `tagColor`
- `hasTagColor`
- `breakMode`
- `ruleHitCount`
- `ruleHitSummary`
- `notes`
- `isFavorite`
