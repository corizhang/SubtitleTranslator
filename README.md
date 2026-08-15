# SubtitleTranslator

Windows 本地 AI 影视字幕识别与翻译工具。当前处于里程碑 0：硬件与模型技术验证。

## 构建

```powershell
dotnet build SubtitleTranslator.slnx
```

## 运行识别基准

准备 whisper.cpp 格式的 GGML 模型后：

```powershell
dotnet run --project tools/SubtitleTranslator.Benchmark -- `
  --media "D:\path\video.mkv" `
  --model "D:\path\ggml-model.bin" `
  --language auto `
  --output benchmark-output
```

程序将自动列出音轨、提取 16 kHz 单声道 WAV、使用 Whisper 转录，并输出 `transcript.json` 和 `transcript.srt`。

当前仓库引用 Whisper.net CUDA 13 runtime；开发机需要 CUDA Toolkit 13.0.1 或更高版本。
发布版本将把 GPU runtime 作为可选部署组件，并保留 CPU 回退方案。

详细设计见：

- [技术基线](docs/technical-baseline.md)
- [开发方案](docs/development-plan.md)
- [GPU 基准手动下载清单](docs/manual-downloads.md)
