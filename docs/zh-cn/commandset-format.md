---
title: SkyCAT 命令集文件格式
parent: 中文文档
nav_order: 4
---

**语言：** [English](../commandset-format.md) | 简体中文

# SkyCAT 命令集文件格式

_更新：2026-08-12_

SkyCAT 把每一种受支持电台的 CAT 命令保存在独立的 **.json** 文件中。文件名就是电台型号，例如 `IC-9700.json` 保存 ICOM IC-9700 的 CAT 命令。

增加新机型支持时，通常只需要创建新的命令集 JSON 文件，而不必修改 SkyCAT 引擎本身。

## 工作模式

不同电台在不同运行状态下，可能使用不同的 CAT 命令。SkyCAT 在初始化时显式设置电台工作模式，然后只使用该模式对应的命令。

目前支持：

- **Duplex**：电台能够同时收发，但通常要求 RX 与 TX 位于不同波段。RX/TX 频率可以随时分别设置；
- **Split**：不能同时收发，但有两个 VFO。软件可以预先分别设置接收和发射频率，电台在 RX/TX 切换时自动切换 VFO；
- **Simplex**：只有一个主要调谐路径。软件需要在 RX/TX 状态改变时自行重新设置频率和模式。

## 顶层文件格式

命令集使用 [JSON](https://www.json.org/) 格式：

```json
{
  "id": 3081,
  "echo": false,
  "default_baud_rate": 115200,
  "cross_band_split": false,
  "bad_reply": ["FE", "FE", "E0", "A2", "FA", "FD"],

  "duplex": {...},
  "split": {...},
  "simplex": {...}
}
```

各字段含义：

- **id**：电台型号 ID。通常使用 Hamlib 的 model ID，以便用户可以用 Hamlib 风格的数字型号选择电台。例如 IC-9700 为 3081；
- **echo**：如果电台会把收到的 CAT/CI-V 命令原样回显，则设为 `true`。如果不回显，或回显可以在电台菜单中关闭，则设为 `false`；
- **default_baud_rate**：该电台命令集默认使用的串口波特率；
- **cross_band_split**：如果电台在 Split 模式下允许 RX/TX 位于不同波段，设为 `true`；
- **bad_reply**：电台拒绝命令时返回的固定错误字节序列。如果没有统一错误响应，可设为 `null`；
- **duplex / split / simplex**：对应不同工作模式的命令集合。**simplex** 通常是基础模式；另外两项只在电台实际支持时提供。

## 命令集合格式

`duplex`、`split`、`simplex` 的内容类似：

```json
{
  "setup": {...},
  "read_rx_frequency": {...},
  "read_tx_frequency": {...},
  "read_rx_mode": {...},
  "read_tx_mode": {...},
  "read_ptt": {...},
  "write_rx_frequency": {...},
  "write_tx_frequency": {...},
  "write_rx_mode": {...},
  "write_tx_mode": {...},
  "write_ptt_off": {...},
  "write_ptt_on": {...},
  "write_ctcss_tone": {...},
  "enable_ctcss": {...},
  "disable_ctcss": {...}
}
```

如果某项命令不被当前电台支持，应写成：

```json
"write_tx_mode": null
```

命令集不要求每一项都存在，但对应工作模式至少要包含足以完成该模式控制的有效命令。

最后三项 CTCSS 命令为可选扩展：

- `write_ctcss_tone`
- `enable_ctcss`
- `disable_ctcss`

它们用于控制**发射端**亚音 / PL tone 编码器，例如访问 SO-50 等 FM 中继卫星。它们不用于接收端亚音解码。

当前 fork 还可以在支持的命令集中定义 Scope 相关命令，例如：

- `enable_scope`
- `disable_scope`
- `enable_scope_data`
- `disable_scope_data`

## 单条命令格式

每个 CAT 命令由如下对象描述：

```json
{
  "messages": [ ... ],
  "alt_messages": [ ... ],
  "restriction": "when_receiving"
}
```

### `messages`

主消息序列。

一个逻辑 CAT 操作可以对应多个底层消息。例如某些机型启用 Split 需要先后发送两条命令。

### `alt_messages`

可选的备用消息序列。

当主 `messages` 被电台拒绝时，SkyCAT 可以尝试备用路径。

IC-9700 SAT 场景是典型例子：当 MAIN/SUB 波段组合发生冲突时，第一次设置频率可能被拒绝，此时可以先交换 MAIN/SUB 再重试。

### `restriction`

可选。用于说明命令在哪一种收发状态下可执行：

- `when_receiving`
- `when_transmitting`
- `when_setting_up`

`when_setting_up` 适合会明显改变 VFO/接收路径的初始化命令，例如交换 VFO。

## Message 格式

`messages` 和 `alt_messages` 中的每个元素可以包含：

```json
{
  "comment": "command description",
  "command": [ ... ],
  "command_param": { ... },
  "reply": [ ... ],
  "reply_param": { ... },
  "ignore_error": true
}
```

字段含义：

- **comment**：可选的人类可读说明，用于日志；
- **command**：要发送给电台的字节序列；
- **command_param**：写命令的参数编码规则；
- **reply**：期待电台返回的字节序列。如果该命令无需等待回复，可以设为 `null`；
- **reply_param**：读命令中返回参数的解码规则；
- **ignore_error**：如果某些电台在命令实际上已经达到期望状态时仍可能返回拒绝，可设为 `true`。

## 字节序列格式

原始字节使用两位十六进制字符串表示，例如：

```json
["FE", "FE", "A2", "E0", "03", "FD"]
```

部分位置可以使用 `null`。

### 写命令中的 `null`

表示该位置会被参数值替换。

例如：

```json
"command": ["4D", "44", null, "3B"]
```

其中 `null` 会由具体 Mode 参数替换。

### Reply 中的 `null`

可以表示：

- 该字节允许为任意值，只在回复匹配时忽略；
- 或者该位置属于需要解析出来的返回参数。

例如 IC-9700 读取模式：

```json
"reply": ["FE", "FE", "E0", "A2", "04", null, null, "FD"]
```

其中一个字节可能是 mode，另一个可能是 filter。

## 参数格式

`command_param` 和 `reply_param` 描述参数的编码/解码方式：

```json
{
  "format": "enum",
  "step": 10,
  "start": 4,
  "length": 1,
  "mask": ["FF", "00"],
  "values": { ... }
}
```

### `format`

支持：

- **BCD_BE**：BCD，大端顺序；
- **BCD_LE**：BCD，小端顺序；
- **text**：ASCII 文本形式的数字；
- **enum**：枚举值。

例如 ICOM 频率通常使用 BCD_LE；某些 Yaesu 命令使用 BCD_BE；Kenwood CAT 常见 ASCII text。

### `step`

可选，默认 1。

如果电台协议以 10 Hz、100 Hz 等步进表达频率，可通过 `step` 指定缩放关系。

例如 `step = 10` 时，协议值 43000001 可以表示 430,000,010 Hz。

### `start` 与 `length`

可选。

当 reply 中存在多个 `null`，但只有其中一部分属于实际返回参数时，用这两个字段指定参数起始位置和长度。

### `mask`

只用于 reply 参数，可选。

解析前会对返回字节与 mask 做按位 AND，用于去掉与目标参数无关的 bit。

### `values`

仅用于 `enum`。

例如：

```json
"values": {
  "LSB": ["00"],
  "USB": ["01"],
  "CW":  ["03"],
  "FM":  ["04"]
}
```

## 编写新命令集时的建议

1. 先查阅电台厂商 CAT / CI-V 手册，而不是只依赖第三方数据库；
2. 明确电台默认地址、echo 行为和串口速率；
3. 先实现 Simplex 的最小命令集；
4. 再增加 Split / Duplex；
5. 对可能改变 MAIN/SUB、VFO 或 SAT 状态的命令使用 `when_setting_up`；
6. 对 FM 卫星需要的机型补充 CTCSS；
7. 用 `skycatd.exe -vvv -f` 收集详细日志；
8. 最终必须使用真实电台验证，而不能只依赖 JSON 结构检查。

另请参阅 [skycatd.exe 命令行说明](skycatd.md)。
