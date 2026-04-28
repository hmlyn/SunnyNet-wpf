# SunnyNet WPF MCP 工具清单

本文档以当前 WPF 版实际实现为准，对应服务端代码：`src/SunnyNet.Wpf/Services/SunnyNetCompatibleMcpServer.cs`。

## 说明

- 主程序内置 HTTP MCP 服务，桥接程序固定从运行目录 `mcp/sunnynet-mcp.exe` 启动。
- 主程序默认**不自动启动 MCP**，需在设置页或底部状态栏手动开启。
- 单会话类工具内部已兼容 `theology` 或 `index` 定位；批量工具仍以 `theologies` 为准。
- 会话列表相关结果已包含：
  - `index`：界面稳定序号
  - `theology`：后端会话唯一 ID
  - `notes`：备注
  - `isFavorite`：是否已收藏
- 部分列表工具支持 `favoritesOnly=true`，可只查看收藏会话。

## 工具分类

### 配置

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `config_get` | 获取当前配置快照 | 无 |

### 代理控制

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `proxy_start` | 启动代理服务 | 无 |
| `proxy_stop` | 停止代理服务 | 无 |
| `proxy_set_port` | 设置监听端口 | `port` |
| `proxy_get_status` | 读取代理状态 | 无 |
| `proxy_set_ie` | 设置系统代理 | 无 |
| `proxy_unset_ie` | 取消系统代理 | 无 |
| `proxy_pause_capture` | 暂停捕获 | 无 |
| `proxy_resume_capture` | 恢复捕获 | 无 |

### 进程

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `process_list` | 获取当前系统进程列表 | 无 |
| `process_add_name` | 添加进程名拦截规则 | `name` |
| `process_remove_name` | 移除进程名拦截规则 | `name` |

### 证书

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `cert_install` | 安装默认 CA 证书 | 无 |
| `cert_export` | 导出默认 CA 证书 | `path` |

### HOSTS 规则

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `hosts_list` | 列出 HOSTS 规则 | 无 |
| `hosts_add` | 添加 HOSTS 规则 | `source`、`target` |
| `hosts_remove` | 删除指定规则 | `index` |

### 替换规则

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `replace_rules_list` | 列出替换规则 | 无 |
| `replace_rules_add` | 添加替换规则 | `type`、`source`、`target` |
| `replace_rules_remove` | 删除替换规则 | `hash` 或 `index` |
| `replace_rules_clear` | 清空替换规则 | 无 |

### 断点 / 拦截规则

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `breakpoint_add` | 添加 URL 正则断点 | `url_pattern` |
| `breakpoint_list` | 列出全部断点 | 无 |
| `breakpoint_remove` | 删除指定断点 | `index` |
| `breakpoint_clear` | 清空全部断点 | 无 |

### 会话列表 / 收藏 / 备注

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `request_list` | 获取请求列表 | `limit`、`offset`、`favoritesOnly` |
| `request_favorites_list` | 直接获取收藏会话列表 | `limit`、`offset` |
| `request_search` | 按 URL/方法/状态筛选请求 | `url`、`method`、`status_code`、`favoritesOnly`、`limit`、`offset` |
| `request_stats` | 获取请求统计 | 无 |
| `request_delete` | 删除指定请求 | `theologies` |
| `request_clear` | 清空请求列表 | 无 |
| `request_clear_by_rule` | 按主界面清除规则删除会话 | `rule` |
| `request_save_all` | 保存全部抓包记录 | `path` |
| `request_import` | 导入 `.syn` 记录 | `path` |
| `request_favorite_add` | 收藏指定会话 | `theology` 或 `index` |
| `request_favorite_remove` | 取消收藏指定会话 | `theology` 或 `index` |
| `request_notes_set` | 设置备注，空字符串表示清除 | `theology` 或 `index`、`notes` |
| `request_tag_set` | 设置会话标记颜色，支持批量 | `theology`/`index`/`theologies`/`indexes`、`color` |
| `request_tag_clear` | 清除会话标记颜色，支持批量 | `theology`/`index`/`theologies`/`indexes` |

### 单会话详情 / 修改 / 重发

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `request_get` | 获取请求详情 | `theology` 或 `index` |
| `request_modify_header` | 修改请求头 | `theology` 或 `index`、`key`、`value` |
| `request_modify_body` | 修改请求体 | `theology` 或 `index`、`body` |
| `response_modify_header` | 修改响应头 | `theology` 或 `index`、`key`、`value` |
| `response_modify_body` | 修改响应体 | `theology` 或 `index`、`body` |
| `request_resend` | 重发请求 | `theologies` |
| `request_block` | 手动阻断请求 | `theology` 或 `index` |
| `request_release_all` | 放行所有断点中的请求 | 无 |
| `request_get_response_body_decoded` | 获取自动解码后的响应体 | `theology` 或 `index` |

### 请求规则中心

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `request_rules_list` | 列出请求规则中心规则 | `type` |
| `request_rules_enable` | 启用或禁用规则 | `type`、`hash`、`enabled` |
| `request_rules_remove` | 删除规则 | `type`、`hash` |
| `request_rule_hits_list` | 列出规则命中记录 | `theology` 或 `index`、`ruleType`、`limit`、`offset` |

`type` 支持：

- `all`
- `http_block`
- `websocket_block`
- `tcp_block`
- `udp_block`
- `rewrite`
- `mapping`

旧 `replace_rules_*` 和 `breakpoint_*` 工具保留兼容，但新规则中心优先使用 `request_rules_*`。

### 搜索 / 高亮

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `request_body_search` | 在 URL / 请求体 / 响应体中搜索 | `keyword`、`scope`、`favoritesOnly`、`limit` |
| `request_highlight_search` | 在 UI 中高亮匹配会话 | `keyword`、`type`、`color` |
| `request_highlight_clear` | 清除搜索高亮 | 无 |

### 长连接 / Socket 数据

| 工具 | 说明 | 关键参数 |
|---|---|---|
| `connection_list` | 列出 WS/TCP/UDP 长连接 | `protocol`、`favoritesOnly`、`limit`、`offset` |
| `socket_data_list` | 获取某个长连接的数据包列表 | `theology` 或 `index`、`limit`、`offset` |
| `socket_data_get` | 获取单个数据包详情 | `theology` 或 `index`、`index` |
| `socket_data_get_range` | 批量获取数据包详情 | `theology` 或 `index`、`start`、`end` |

## 常用参数约定

### 会话定位

- `theology`：后端原始会话 ID
- `index`：界面显示序号
- 单会话工具内部可直接传 `index`
- 批量工具仍使用 `theologies: int[]`

### `favoritesOnly`

支持该参数的工具：

- `request_list`
- `request_search`
- `request_body_search`
- `connection_list`

### `scope`

`request_body_search` 的搜索范围：

- `url`
- `request_body`
- `response_body`
- `all`

### 替换规则 `type`

`replace_rules_add` 支持：

- `Base64`
- `HEX`
- `String(UTF8)`
- `String(GBK)`
- `响应文件`

## 返回字段补充

常见会话列表结果会带以下字段：

| 字段 | 说明 |
|---|---|
| `index` | 界面序号 |
| `theology` | 后端会话 ID |
| `method` | 请求方式 |
| `url` | 请求地址 |
| `statusCode` | 状态码 |
| `pid` | 进程信息 |
| `clientIP` | 客户端 IP |
| `sendTime` | 发起时间 |
| `recTime` | 接收时间 |
| `way` | 协议类型 |
| `host` | Host |
| `query` | Query |
| `length` | 响应长度 |
| `type` | 响应类型 |
| `process` | 进程信息 |
| `tagColor` | 会话标记颜色 |
| `hasTagColor` | 是否存在标记颜色 |
| `breakMode` | 断点/拦截状态 |
| `ruleHitCount` | 命中规则数量 |
| `ruleHitSummary` | 命中规则摘要 |
| `notes` | 备注 |
| `isFavorite` | 收藏状态 |

## 常用调用示例

### 只看收藏列表

```json
{
  "name": "request_favorites_list",
  "arguments": {
    "limit": 20,
    "offset": 0
  }
}
```

### 收藏某条会话

```json
{
  "name": "request_favorite_add",
  "arguments": {
    "index": 5
  }
}
```

### 设置备注

```json
{
  "name": "request_notes_set",
  "arguments": {
    "index": 5,
    "notes": "登录接口，已确认命中测试账号"
  }
}
```

### 只搜索收藏会话里的关键字

```json
{
  "name": "request_body_search",
  "arguments": {
    "keyword": "token",
    "scope": "all",
    "favoritesOnly": true,
    "limit": 20
  }
}
```
