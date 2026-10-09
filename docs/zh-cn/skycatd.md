---
title: skycatd.exe 命令行
parent: 中文文档
nav_order: 3
---

**语言：** [English](../skycatd.md) | 简体中文

# skycatd.exe

**skycatd.exe** 是一个基于 SkyCAT 类库的命令行程序。它通过串口连接电台，并通过 TCP 向客户端提供 CAT 控制接口。

当前 fork 还提供两个仅监听本机回环地址的附加服务：

- 面向 WSJT-X 的受限 Hamlib NET rigctl 兼容代理；
- 面向 SkyRoof 的 IC-9700 原生二进制频谱帧流。

## 安装

无需安装程序。下载发布包后，将所有文件解压到同一目录即可。

电台命令集文件存放在 **Rigs** 子目录中。传给 `--model` 的型号必须与其中某个命令集文件名相同，或者使用该命令集的数字 Model ID。

如果使用依赖系统运行时的构建，请安装
[.NET 9.0 Desktop Runtime](https://learn.microsoft.com/dotnet/core/install/)。

Windows：

```bash
winget install Microsoft.DotNet.DesktopRuntime.9
```

macOS：

```bash
brew install dotnet
```

Linux（Ubuntu）：

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0
```

## 快速开始

假设 IC-9700 连接在 `COM9`：

```bash
skycatd.exe -m IC-9700 -r COM9
```

IC-9700 命令集已经把默认波特率定义为 115200，因此上面的命令等价于：

```bash
skycatd.exe -m IC-9700 -r COM9 -s 115200
```

使用默认参数时，skycatd 会启动以下监听端口：

| 服务 | 默认监听地址 | 默认端口 |
|------|--------------|---------:|
| SkyCAT 主 CAT 服务 | `127.0.0.1` | 4532 |
| WSJT-X 兼容代理 | `127.0.0.1` | 4534 |
| IC-9700 频谱流 | `127.0.0.1` | 4535 |

主 CAT 服务默认只允许本机连接。只有显式使用 `--allow-remote` 时，它才会监听所有网络接口。

## 命令行参数

一个较完整的启动示例：

```bash
skycatd.exe -m IC-9700 -r COM9 -s 115200 -t 4532 -vvv -f
```

### `-m, --model <model>`

正常启动服务器时为必填参数。

选择电台命令集。参数可以是：

- `Rigs` 目录下 JSON 文件的不带扩展名的文件名，例如 `IC-9700`；
- `skycatd.exe -l` 输出的数字 Model ID。

例如：

```bash
skycatd.exe -m IC-9700 -r COM9
skycatd.exe -m 3081 -r COM9
```

两者都会选择 IC-9700。

### `-r, --rig-file <serial-port>`

正常启动服务器时为必填参数。

指定连接电台的串口。虽然长参数名仍然叫 `--rig-file`，当前实现实际上直接把这个字符串作为串口名使用。

Windows：

```bash
-r COM9
```

Linux：

```bash
-r /dev/ttyUSB0
```

### `-s, --serial-speed <baud>`

可选。

覆盖命令集里定义的默认波特率。

例如：

```bash
skycatd.exe -m IC-9700 -r COM9 -s 115200
```

不指定时，skycatd 使用对应 JSON 命令集中的 `default_baud_rate`。

### `-t, --port <port>`

可选。默认值：**4532**。

指定 SkyCAT 主 CAT TCP 服务端口。

例如：

```bash
skycatd.exe -m IC-9700 -r COM9 -t 4600
```

默认监听：

```text
127.0.0.1:<port>
```

如需允许局域网内其他主机连接，请额外使用 `--allow-remote`。

### `--allow-remote`

可选。默认：**关闭**。

允许主 CAT 服务监听所有网络接口。

默认情况下：

```text
127.0.0.1:4532
```

使用：

```bash
skycatd.exe -m IC-9700 -r COM9 --allow-remote
```

后，主 CAT 服务监听：

```text
0.0.0.0:4532
```

这个参数**只影响主 CAT 服务**。WSJT-X 代理和 Scope stream 仍然只监听 `127.0.0.1`。

> 主 CAT TCP 协议本身不提供 TLS 或用户认证。不要直接把它暴露到公网。远程控制应使用受信任局域网、防火墙规则、VPN 或受保护的隧道。

### `--wsjtx-port <port>`

可选。默认值：**4534**。

设置 WSJT-X Hamlib NET rigctl 兼容代理的端口。

例如：

```bash
skycatd.exe -m IC-9700 -r COM9 --wsjtx-port 4540
```

无论是否使用 `--allow-remote`，WSJT-X 代理始终仅监听 `127.0.0.1`。

### `--no-wsjtx-proxy`

可选。默认：**关闭**。

完全禁用 WSJT-X 兼容代理。

```bash
skycatd.exe -m IC-9700 -r COM9 --no-wsjtx-proxy
```

主 CAT 服务和 Scope stream 不受影响。

### `--scope-port <port>`

可选。默认值：**4535**。

指定 SkyRoof 使用的 IC-9700 原生二进制频谱流端口。

例如：

```bash
skycatd.exe -m IC-9700 -r COM9 --scope-port 4605
```

Scope server 始终只监听 `127.0.0.1`。

每个 Scope 消息由以下两部分组成：

1. 4 字节 little-endian 无符号帧长度；
2. 对应长度的原始 CI-V frame。

该端口是**二进制数据端口**，不能当成普通 CAT 或 rigctl 文本端口使用。

### `-v, --verbose`

可选。

启用详细日志。历史用法通常写成多个 `v`：

```bash
skycatd.exe -m IC-9700 -r COM9 -vvv
```

当前实现中，只要指定 verbose 参数，就启用 Serilog 的 **Verbose** 最低日志级别；`v` 的数量不会继续细分日志等级。

不指定时，最低日志级别为 **Warning**。

### `-f, --file-log`

可选。默认：**关闭**。

除控制台外，同时把日志保存到文件。

```bash
skycatd.exe -m IC-9700 -r COM9 -vvv -f
```

Windows 下会在当前工作目录的 `Logs` 子目录中创建类似：

```text
Logs\skycatd_2026-09-29_080000.log
```

启动时会打印日志文件的完整路径。

### `-l, --list`

列出当前构建包含的所有电台型号与 Model ID，然后退出。

```bash
skycatd.exe -l
```

输出类似：

```text
Rig #    Model
3081     IC-9700
...
```

使用此参数时，不需要 `--model` 和 `--rig-file`。

### `-a, --all`

输出所有已加载电台命令集的 capability 信息，然后退出。

```bash
skycatd.exe -a
```

输出为 JSON 格式。

使用此参数时，不需要 `--model` 和 `--rig-file`。

### `--help`

显示 CommandLineParser 生成的命令行帮助并退出。

```bash
skycatd.exe --help
```

### `--version`

显示版本信息并退出。

```bash
skycatd.exe --version
```

## 端口规则

所有 TCP 端口必须在 **1-65535** 范围内。

当 WSJT-X proxy 启用时，下列三个端口必须互不相同：

- 主 CAT：`--port`；
- WSJT-X proxy：`--wsjtx-port`；
- Scope stream：`--scope-port`。

例如以下配置无效：

```bash
skycatd.exe -m IC-9700 -r COM9 -t 4534 --wsjtx-port 4534
```

如果使用 `--no-wsjtx-proxy`，WSJT-X 端口不再参与冲突检查；但主 CAT 端口与 Scope 端口仍然必须不同。

## 当前支持的电台

当前 fork 内置以下命令集：

| 型号 | Model ID | 默认波特率 |
|------|---------:|-----------:|
| FT-817 | 1020 | 38400 |
| FT-818 | 1041 | 38400 |
| FT-847 | 1001 | 57600 |
| FT-897 | 1023 | 38400 |
| FT-991A | 1035 | 38400 |
| IC-705 | 3085 | 115200 |
| IC-705-wireless | 30850 | 19200 |
| IC-706MKIIG | 3011 | 19200 |
| IC-905 | 3090 | 115200 |
| IC-910 | 3044 | 19200 |
| IC-9100 | 3068 | 19200 |
| IC-9700 | 3081 | 115200 |
| IC-R7000 | 3040 | 1200 |
| TS-2000 | 2014 | 57600 |

对于你实际运行的版本，应以：

```bash
skycatd.exe -l
```

的输出为准。

## 常用配置

### SkyRoof + IC-9700

本机 SkyRoof 的常规配置：

```bash
skycatd.exe -m IC-9700 -r COM9
```

此时：

```text
SkyRoof CAT:     127.0.0.1:4532
WSJT-X proxy:    127.0.0.1:4534
Scope stream:    127.0.0.1:4535
```

如果电台使用其他 COM 口，例如 COM12：

```bash
skycatd.exe -m IC-9700 -r COM12
```

### SkyRoof + WSJT-X

启动：

```bash
skycatd.exe -m IC-9700 -r COM9
```

SkyRoof 连接：

```text
127.0.0.1:4532
```

WSJT-X 连接：

```text
127.0.0.1:4534
```

具体 WSJT-X 设置见后文 [WSJT-X 兼容代理](#wsjt-x-兼容代理)。

### 允许局域网其他电脑连接主 CAT

```bash
skycatd.exe -m IC-9700 -r COM9 --allow-remote
```

如果 Windows Firewall 弹出提示，只应放行实际需要使用的网络配置文件。

WSJT-X 和 Scope 端口仍然只对运行 skycatd 的本机开放。

### 自定义三个 TCP 端口

```bash
skycatd.exe -m IC-9700 -r COM9 \
  --port 4600 \
  --wsjtx-port 4601 \
  --scope-port 4602
```

### 禁用 WSJT-X

```bash
skycatd.exe -m IC-9700 -r COM9 --no-wsjtx-proxy
```

### 调试日志

```bash
skycatd.exe -m IC-9700 -r COM9 -vvv -f
```

排查 CAT、CI-V、PTT 或 Scope 问题时，推荐使用这一形式。

## 运行 skycatd

Windows：

```bash
skycatd.exe <parameters>
```

Linux / macOS：

```bash
dotnet skycatd.dll <parameters>
```

按 **Ctrl-C** 停止服务器。skycatd 会先停止接受新的客户端，释放由 SkyCAT 自己持有的 PTT，然后停止 Scope stream 并关闭串口。

如果运行过程中串口暂时不可用，服务器会周期性尝试重新打开；串口恢复后，TCP listener 也会重新启动。

## Skycatd TCP 命令

主 SkyCAT TCP 服务接受以下以换行符结束的文本命令：

| 操作 | 命令 |
|------|------|
| setup(Duplex) | `U Duplex` |
| setup(Split) | `U Split` |
| setup(Simplex) | `U Simplex` |
| 读取 RX 频率 | `f` |
| 读取 TX 频率 | `i` |
| 写 RX 频率 | `F {frequency}` |
| 写 TX 频率 | `I {frequency}` |
| 读取 RX 模式 | `m` |
| 读取 TX 模式 | `x` |
| 写 RX 模式 | `M {mode} 0` |
| 写 TX 模式 | `X {mode} 0` |
| 读取 PTT | `t` |
| PTT ON | `T 1` |
| PTT OFF | `T 0` |
| 设置发射 CTCSS | `C {tone}` |
| 开启 CTCSS 编码 | `U TONE 1` |
| 关闭 CTCSS 编码 | `U TONE 0` |
| 开启 Scope | `U SCOPE 1` |
| 关闭 Scope | `U SCOPE 0` |
| 开启 Scope 数据 | `U SCOPE_DATA 1` |
| 关闭 Scope 数据 | `U SCOPE_DATA 0` |
| IC-9700 Scope FAST | `U SCOPE_FAST 1` |
| 设置 IC-9700 硬件 RF 增益（0–255） | `U RF_GAIN 128` |
| 读取 IC-9700 硬件 RF 增益（十进制 0–255） | `U RF_GAIN_READ` |
| 读取 MAIN/SUB 频谱配置 | `U SCOPE_READ` |
| 选择 MAIN/SUB | `U SCOPE_SELECT MAIN|SUB` |
| 中心/固定/滚动频谱模式 | `U SCOPE_MODE MAIN|SUB CENTER|FIXED|SCROLL-C|SCROLL-F` |
| 中心模式扫描跨度（Hz） | `U SCOPE_SPAN MAIN|SUB 25000` |
| 固定频谱边界编号 | `U SCOPE_EDGE MAIN|SUB 1..4` |
| 参考电平（-20～20 dB，0.5 dB 步进） | `U SCOPE_REF MAIN|SUB -12.5` |
| 扫描速度 | `U SCOPE_SPEED MAIN|SUB FAST|MID|SLOW` |
| 视频带宽 | `U SCOPE_VBW MAIN|SUB NARROW|WIDE` |
| 发射时显示频谱 | `U SCOPE_TX 0|1` |
| 频谱中心频率定义 | `U SCOPE_CENTER_TYPE FILTER|CARRIER|ABS` |
| 标记位置 | `U SCOPE_MARKER FILTER|CARRIER` |
| 读取固定频谱边界 | `U SCOPE_READ_EDGE 2 1` |
| 设置固定频谱边界（Hz） | `U SCOPE_FIXED_EDGE 2 1 435000000 436000000` |

其中：

- **frequency** 单位为 Hz；
- **tone** 使用 0.1 Hz 为单位。

例如：

```text
C 670
```

表示选择 **67.0 Hz** CTCSS。

为了兼容 rigctld 客户端，还识别以下 setup aliases：

```text
U SATMODE 1
S 1 VFOB
S 0 VFOB
```

分别映射到 Duplex、Split 和 Simplex setup。


### SkyRoof 频谱控制握手

频谱数据流（TCP 4535）和普通 CAT 命令端口（默认 4534）相互独立。SkyRoof 的
`Scope control path = SkyCAT` 必须连接 SkyCAT 普通 CAT 服务；启动时须指定
`--model IC-9700`，并保持串口 CI-V 通路连接。切换频谱数据来源并不等于切换控制通道。

`U SCOPE_READ` 从电台实时读取 MAIN/SUB 频谱状态，不返回伪造默认值，
输出用分号分隔的字段，例如：

```text
SELECT=MAIN;MAIN.MODE=CENTER;MAIN.SPAN=25000;MAIN.EDGE=1;MAIN.REF=15.0;MAIN.SPEED=FAST;MAIN.VBW=WIDE;SUB.MODE=CENTER;SUB.SPAN=25000;SUB.EDGE=1;SUB.REF=15.0;SUB.SPEED=FAST;SUB.VBW=WIDE;TX=0;CENTER=FILTER;MARKER=FILTER
```

`U SCOPE_READ_EDGE 2 1` 返回真实固定边界：
`RANGE=2;EDGE=1;LOWER=435000000;UPPER=436000000`。
无效参数返回 `RPRT -1`，电台拒绝指令返回 `RPRT -9`，
串口未连接返回 `RPRT -6`。发送队列接受命令不代表电台已接受写操作；
必须以 CI-V FB 应答及再次读回为准。

## WSJT-X 兼容代理

当前 fork 提供一个专门面向 WSJT-X 的受限 Hamlib NET rigctl 兼容端口。

代理支持 Hamlib 初始化查询 `\chk_vfo`、`\dump_state`，支持频率/模式/PTT 读取以及 CAT PTT。

对于频率、模式、VFO、Split、SAT mode、CTCSS 等可能与 SkyRoof 多普勒控制冲突的写操作，代理会返回成功但**不把这些写操作转发给电台**。这样可以避免 WSJT-X/Hamlib 在初始化或切换波段时进入 Radio Fault，同时保持 SkyRoof 是唯一的调谐/多普勒控制者。

如果某个 WSJT-X 客户端通过 CAT 打开了 PTT，随后客户端断开，SkyCAT 会尝试释放该客户端持有的 PTT。

WSJT-X proxy 永远只监听本机回环接口。

### WSJT-X 设置

打开 **File -> Settings -> Radio**：

- **Rig:** Hamlib NET rigctl
- **Network Server:** `127.0.0.1:4534`
- **PTT Method:** CAT
- **Mode:** None
- **Split Operation:** None

SkyRoof 仍应直接连接主 SkyCAT 端口：

```text
127.0.0.1:4532
```

## IC-9700 Scope Stream

频谱数据使用独立的二进制端口，不与普通的逐行 CAT 文本协议混用。

默认：

```text
127.0.0.1:4535
```

SkyCAT 从共享串口 CI-V 数据流中提取完整的 IC-9700 `27 00` waveform frame，并转发给 Scope 客户端。

每个客户端都有有界队列。若某个客户端读取过慢，旧频谱帧可以被丢弃，从而避免 Scope 流反向阻塞 CAT 命令处理。

因此该端口的定位是**实时显示**，而不是无损录制。

## 注意事项

- 主 CAT 服务默认仅监听 loopback；
- `--allow-remote` 不会暴露 WSJT-X 和 Scope listener；
- `--model` 决定可用 CAT 命令和默认串口速率；
- Duplex、Split、Simplex 下可用的命令可能不同；
- 客户端不应假设所有电台都支持所有 CAT 命令；
- 使用 IC-9700 + SkyRoof 时，通常保持默认 4532 / 4534 / 4535 即可。


### IC-9700 Remote Control Switch 专用 TCP 端口

SkyCAT 为 Remote Control Switch 新增仅监听本机的
`127.0.0.1:4536` TCP 接口。通过 `--switch-port` 修改端口；
通过 `--no-switch-port` 关闭。仅在电台型号为 IC-9700 时启动，
并禁止与 CAT/WSJT-X/频谱端口重号。

协议为逐行 ASCII：`GET DATA_OFF` 返回 `VALUE 05`；
`SET DATA_OFF 05` 成功返回 `OK`。其他允许的名称包括
`DATA_MOD`、`USB_OUTPUT`、`COMP`、`COMP_LEVEL`、
`KEY_SPEED`、`RF_POWER`；`SAT_MODE` **只允许读取**。

使用 IC-9700 官方 CI-V 选择器 `1A 05 01 15/16/05`、
`16 44`、`14 0E/0C/0A`、`16 5A`。
数据值保留原始十六进制形式；`14` 的数值为两字节 BCD
`0000..0255`，不直接传十进制百分比。

专用端口**不提供原始 CI-V 透传**，拒绝 PTT、VFO/MAIN/SUB
切换、卫星模式写入、频率及工作模式修改。所有命令共享 SkyCAT
原有的串口命令锁，不额外连接电台或占用 COM。
SkyRoof 继续负责卫星跟踪及多普勒，RS-BA1 配置不变。
分开的 TCP 端口并不代表拥有独立的硬件控制链路。
