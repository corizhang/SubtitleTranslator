# 本地 GPU 基准：手动下载清单

> 适用机器：GTX 1660 SUPER 6 GB、NVIDIA 驱动 595.71、Windows 10 x64  
> 检测到的驱动 CUDA 能力：13.2

## 1. CUDA Toolkit

安装 NVIDIA CUDA Toolkit 13.2.x。Whisper.net 1.9.1 的 CUDA runtime 要求 Toolkit
不低于 13.0.1；13.2 与本机驱动报告的 CUDA 版本一致。

- 官方下载页：<https://developer.nvidia.com/cuda-downloads?target_arch=x86_64&target_os=Windows&target_type=exe_local&target_version=10>
- 官方历史版本页：<https://developer.nvidia.com/cuda-toolkit-archive>

在页面中选择：

```text
Operating System: Windows
Architecture: x86_64
Version: 10
Installer Type: exe (local)
```

如果当前下载页已经切换到高于 13.2 的版本，从历史版本页选择 CUDA Toolkit 13.2.1。

使用默认安装位置：

```text
C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.2
```

不需要重新安装显卡驱动；安装器若提供驱动组件，可以取消该组件并只安装 Toolkit。
安装完成后应能在新终端中运行：

```powershell
nvcc --version
```

## 2. Whisper 模型

模型来源：whisper.cpp 官方 Hugging Face 模型仓库：

- <https://huggingface.co/ggerganov/whisper.cpp>

把以下三个文件保存到项目的 `models` 目录：

```text
D:\000-Inbox\001-Documents\ChatGPT\字幕翻译\models\
```

### 快速模式

- 文件：`ggml-small-q5_1.bin`
- 大小：约 181 MiB
- 直接下载：<https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin?download=true>
- SHA-1：`6fe57ddcfdd1c6b07cdcc73aaf620810ce5fc771`

### 均衡模式

- 文件：`ggml-medium-q5_0.bin`
- 大小：约 514 MiB
- 直接下载：<https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium-q5_0.bin?download=true>
- SHA-1：`7718d4c1ec62ca96998f058114db98236937490e`

### 高质量候选

- 文件：`ggml-large-v3-turbo-q5_0.bin`
- 大小：约 547 MiB
- 直接下载：<https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin?download=true>
- SHA-1：`e050f7970618a659205450ad97eb95a18d69c9ee`

这些都是多语言模型，不要下载文件名带 `.en` 的英语专用版本，以便后续测试其他外语。

## 3. Whisper.net CUDA runtime

## 3. Silero VAD 模型

- 文件：`ggml-silero-v6.2.0.bin`
- 大小：885,098 字节
- 直接下载：<https://huggingface.co/sandrohanea/whisper.net/resolve/main/vad/ggml-silero-v6.2.0.bin?download=true>
- SHA-256：`2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987`
- 保存位置：`D:\000-Inbox\001-Documents\ChatGPT\字幕翻译\models\ggml-silero-v6.2.0.bin`

## 4. Whisper.net CUDA runtime

项目需要的 NuGet 包：

- 包页面：<https://www.nuget.org/packages/Whisper.net.Runtime.Cuda/1.9.1>
- Windows 原生包页面：<https://www.nuget.org/packages/Whisper.net.Runtime.Cuda.Windows/1.9.1>

通常不需要手动安装或解压 NuGet 包。CUDA Toolkit 和模型准备完毕后，由开发命令
`dotnet restore` 把 `Whisper.net.Runtime.Cuda` 恢复到用户 NuGet 缓存。若自动恢复仍然失败，
再从 NuGet 页面下载 `.nupkg` 到：

```text
D:\000-Inbox\001-Documents\ChatGPT\字幕翻译\packages-local\
```

不要把 `.nupkg` 解压到 `src` 或应用输出目录。

## 5. 下载完成后的检查

目录应类似：

```text
models/
  ggml-small-q5_1.bin
  ggml-medium-q5_0.bin
  ggml-large-v3-turbo-q5_0.bin
```

可使用以下命令校验 SHA-1：

```powershell
Get-FileHash .\models\*.bin -Algorithm SHA1
```
