# 轻量 Windows 安装器

安装器只包含 framework-dependent Windows x64 应用，不包含 Whisper 模型、FFmpeg、VAD、Whisper runtime 或原生 LibVLC runtime。应用只携带轻量的 LibVLCSharp 托管适配层；用户可在安装后选择已有的 64 位 VLC。

构建前先发布应用，然后使用 WiX 5：

```powershell
dotnet publish src/SubtitleTranslator.App/SubtitleTranslator.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish-win-x64 -p:DebugSymbols=false -p:DebugType=None
$publishPath = (Resolve-Path artifacts/publish-win-x64).Path
wix build installer/Product.wxs -b "publish=$publishPath" -o artifacts/installer/SubtitleTranslator-0.25.1-win-x64.msi
```

目标电脑需要预先安装 .NET Desktop Runtime 10。其余依赖在应用首次配置向导中检测和选择。
