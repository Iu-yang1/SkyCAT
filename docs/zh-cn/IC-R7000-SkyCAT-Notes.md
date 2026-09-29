---
title: IC-R7000 SkyCAT 驱动笔记
parent: 中文文档
nav_order: 7
---

**语言：** [English](../IC-R7000-SkyCAT-Notes.md) | 简体中文

# IC-R7000 SkyCAT 驱动

_开发笔记与社区贡献指南_

原文由 N1BAQ 编写。

## 背景

Icom IC-R7000 宽带接收机原本没有 SkyCAT 命令定义文件，因此无法直接使用 SkyRoof 的原生 CAT 控制。

这个驱动的目标是让 SkyRoof 能够控制 R7000，并在卫星过境时进行多普勒频率修正。

## 为什么不直接使用 rigctld / Hamlib？

最初使用 Hamlib 的 `rigctld`，因为它把 R7000 列为 model 3040。

基本通信确实能够工作，例如：

```bash
rigctl -m 3040 -r COM4 -s 1200
```

可以读取频率。

但是与 SkyRoof 配合时出现问题：

- SkyRoof 初始化时会发送用于选择 VFO 的命令；
- R7000 是纯接收机，没有普通意义上的 VFO A/B 结构，因此拒绝相关命令；
- rigctld 返回错误后，SkyRoof 会断开并不断重新连接。

SkyCAT 更适合这种情况，因为可以通过自定义 command-set 文件，只定义 R7000 真正支持的命令。

## 最初的错误方向

最开始以 IC-706MKIIG 的 JSON 为基础修改，并使用 Hamlib 数据库里给 R7000 标出的 CI-V 地址 **52h**。

**这个地址是错误的，并导致了很长时间的排查。**

使用 52h 时，R7000 会在 CI-V 总线上回显收到的数据，因此表面上看起来链路是通的；但是电台不会真正执行发送给错误地址的命令。

典型现象是：

- SkyCAT 可以连接；
- setup 看起来完成；
- set frequency 返回 OK；
- 但 R7000 显示频率完全不改变。

根据 R7000 自己的说明书，它的默认 CI-V 地址实际上是：

**08h，而不是 52h。**

因此为老式 ICOM 编写驱动时，应优先查阅设备原厂说明书。

## 关键发现

### 1. CI-V 地址是 08h，不是 52h

这是最关键的一点。

R7000 手册明确给出默认 CI-V 地址 **08h**。如果使用 52h，电台虽然可能回显命令，但不会真正执行这些命令。

### 2. 多数命令需要 `reply: null`

R7000 使用 1200 Baud，通信速度非常低。

对于这类老设备，如果 SkyCAT 等待标准确认帧，可能在预期时间内收不到完整回复。因此这个驱动对相关命令使用：

```json
"reply": null
```

让命令以 fire-and-forget 方式工作。

### 3. Setup 也需要 `reply: null`

SkyCAT 的 Simplex 命令组需要 setup 项。

可以使用读取频率作为初始化 ping，但对于 R7000 同样使用：

```json
"reply": null
```

以避免慢速 1200 Baud 接口导致初始化超时。

### 4. `echo: true` 是正确设置

老式 ICOM CI-V 设备通常会把发送的命令回显到总线。

因此 R7000 应设置：

```json
"echo": true
```

SkyCAT 会先识别并丢弃命令 echo，再继续处理后续数据。

### 5. 后面板 REMOTE 开关必须打开

R7000 后面板的 **REMOTE** 开关必须处于 **ON**。

如果关闭，电台仍可能回显 CI-V 数据，但不会执行控制命令。这个现象和 CI-V 地址错误非常相似，因此很容易误判。

### 6. SkyRoof CAT Delay

初次配置 R7000 时，建议把 SkyRoof CAT Control 中的 Delay 设为约 **500-1000 ms**。

1200 Baud 接口远慢于现代电台，不适合使用过于激进的轮询或写命令节奏。

## 最终可工作的 IC-R7000.json

把下面的文件保存为 `IC-R7000.json` 并放入 SkyCAT 的 `Rigs` 目录：

```json
{
  "id": 3040,
  "echo": true,
  "default_baud_rate": 1200,
  "cross_band_split": false,
  "bad_reply": [
    "FE",
    "FE",
    "E0",
    "08",
    "FA",
    "FD"
  ],
  "comment": "Icom IC-R7000. CI-V address 08h (NOT 52h as in hamlib). reply:null required due to 1200 baud timeout.",
  "simplex": {
    "setup": {
      "messages": [
        {
          "command": [
            "FE",
            "FE",
            "08",
            "E0",
            "03",
            "FD"
          ],
          "reply": null,
          "comment": "Read frequency as setup ping"
        }
      ],
      "restriction": "when_setting_up"
    },
    "read_rx_frequency": {
      "messages": [
        {
          "command": [
            "FE",
            "FE",
            "08",
            "E0",
            "03",
            "FD"
          ],
          "reply": null
        }
      ],
      "restriction": "when_receiving"
    },
    "read_tx_frequency": null,
    "read_rx_mode": {
      "messages": [
        {
          "command": [
            "FE",
            "FE",
            "08",
            "E0",
            "04",
            "FD"
          ],
          "reply": null
        }
      ],
      "restriction": "when_receiving"
    },
    "read_tx_mode": null,
    "read_ptt": null,
    "write_rx_frequency": {
      "messages": [
        {
          "command": [
            "FE",
            "FE",
            "08",
            "E0",
            "05",
            null,
            null,
            null,
            null,
            null,
            "FD"
          ],
          "reply": null,
          "command_param": {
            "format": "BCD_LE"
          }
        }
      ],
      "restriction": "when_receiving"
    },
    "write_tx_frequency": null,
    "write_rx_mode": {
      "messages": [
        {
          "command": [
            "FE",
            "FE",
            "08",
            "E0",
            "06",
            null,
            "FD"
          ],
          "reply": null,
          "command_param": {
            "format": "Enum",
            "values": {
              "LSB": ["00"],
              "USB": ["01"],
              "AM": ["02"],
              "CW": ["03"],
              "RTTY": ["04"],
              "FM": ["05"],
              "WFM": ["06"]
            }
          }
        }
      ],
      "restriction": "when_receiving"
    },
    "write_tx_mode": null,
    "write_ptt_off": null,
    "write_ptt_on": null
  }
}
```

## 快速配置

第一次把 IC-R7000 与 SkyRoof / SkyCAT 配合使用时：

- **硬件：** 使用 CT-17 或兼容 CI-V 接口连接 R7000 与电脑；
- **REMOTE：** R7000 后面板 REMOTE 开关设为 ON；
- **SkyCAT：** 确认 `Rigs/IC-R7000.json` 存在；
- **启动：**

```bash
skycatd.exe -m IC-R7000 -r COM4 -s 1200 -t 4532
```

根据实际情况修改 COM 口。

SkyRoof CAT Control 建议：

- RX CAT Host：`127.0.0.1`
- RX CAT Port：`4532`
- Enabled：`True`
- Radio Type：`Simplex`
- Delay：初始可设为约 `500 ms`

正常情况下，SkyRoof 状态栏中的 RX CAT 指示应变为绿色，并且 R7000 的频率应随卫星多普勒修正而变化。

## 贡献与问题反馈

### 当前 SkyCAT fork

当前维护仓库：

[https://github.com/Iu-yang1/SkyCAT](https://github.com/Iu-yang1/SkyCAT)

如果修改或改进 R7000 命令集，可以直接向该仓库提交 issue 或 pull request。

### 上游 SkyCAT

上游项目：

[https://github.com/VE3NEA/SkyCAT](https://github.com/VE3NEA/SkyCAT)

### SkyRoof 社区

SkyRoof Google Group：

[https://groups.google.com/g/skyroof](https://groups.google.com/g/skyroof)

### Hamlib

如果要报告 Hamlib 对 R7000 CI-V 地址定义的问题：

[https://github.com/Hamlib/Hamlib/issues](https://github.com/Hamlib/Hamlib/issues)

报告时应明确指出 R7000 原厂文档使用 **08h**，并附上可复现的测试结果。

---

_原始驱动由 N1BAQ（FN41QO）开发并实机验证，最初验证环境为 SkyCAT 1.6 / SkyRoof 1.29，2026 年 5 月。_
