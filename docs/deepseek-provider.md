# DeepSeek 翻译 Provider

> 更新日期：2026-08-15

## 当前配置

- OpenAI 格式基地址：`https://api.deepseek.com`
- 接口：`POST /chat/completions`
- 默认模型：`deepseek-v4-flash`
- 输出模式：`response_format: { "type": "json_object" }`
- 思考模式：关闭；字幕翻译不需要额外推理过程
- API Key：只读取当前进程的 `DEEPSEEK_API_KEY` 环境变量，不写入项目、日志或命令行参数

官方资料：

- [Chat Completion API](https://api-docs.deepseek.com/api/create-chat-completion)
- [JSON Output](https://api-docs.deepseek.com/guides/json_mode/)
- [错误码](https://api-docs.deepseek.com/quick_start/error_codes/)
- [模型与价格](https://api-docs.deepseek.com/quick_start/pricing/)

## 临时设置 API Key

在准备运行程序的同一个 PowerShell 窗口执行：

```powershell
$env:DEEPSEEK_API_KEY = "你的 API Key"
```

该变量只在当前 PowerShell 进程及其子进程中有效。不要把真实密钥写入文档、源码、`.env`、
命令参数或提交到 Git。项目已忽略 `.env`，当前开发版本支持将它作为环境变量之后的回退来源。

也可以直接编辑项目根目录的 `.env`：

```dotenv
DEEPSEEK_API_KEY=你的 API Key
```

读取优先级为：进程环境变量、Windows 用户环境变量、项目根目录 `.env`。`.env` 是明文文件，
只应用于当前开发阶段；WPF 凭据界面完成后迁移到 Windows Credential Manager 并删除此文件。

如果程序已经从另一个进程启动（例如 Codex 桌面应用），当前 PowerShell 的 `$env:` 不会传播给它。
可临时写入 Windows 当前用户环境变量，程序会在进程变量缺失时回退读取：

```powershell
[Environment]::SetEnvironmentVariable("DEEPSEEK_API_KEY", "你的 API Key", "User")
```

完成真实验证后可清除持久值：

```powershell
[Environment]::SetEnvironmentVariable("DEEPSEEK_API_KEY", $null, "User")
```

## 运行 30 秒验证样本

```powershell
dotnet run --project tools/SubtitleTranslator.Benchmark -- `
  --media samples/private/benchmark-30s.mkv `
  --model models/ggml-small-q5_1.bin `
  --language en `
  --no-context `
  --deepseek-translation `
  --output samples/private/bench-30s-deepseek
```

成功后会生成：

- `transcript.srt`：原文字幕
- `chinese.srt`：DeepSeek 中文字幕
- `bilingual.srt`：原文在上、中文在下
- `translation-cache/*.json`：已完成且通过 SegmentId 校验的批次缓存

再次使用相同模型、提示上下文和原文时会命中缓存，不重复产生 API 费用。

## 错误和重试

- HTTP 429、500、502、503、504、408：指数退避并加入随机抖动，最多尝试 4 次；优先遵守服务端 `Retry-After`。
- 网络错误和非用户取消的超时：自动重试。
- HTTP 400、401、402、422：直接失败，避免错误请求反复计费。
- `finish_reason` 不是 `stop`、空内容、无 `translations` 数组：拒绝进入字幕文件。
- 漏段、重复 SegmentId、未知 SegmentId、空译文：拒绝整个批次，不写入缓存。
