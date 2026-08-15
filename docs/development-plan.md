# AI 影视字幕翻译工具：开发方案

> 依赖文档：[技术基线](technical-baseline.md)  
> 日期：2026-08-13

## 1. 开发策略

采用“先验证高风险能力，再搭完整 UI”的顺序。GPU 推理、字幕时间映射和视频同步预览是最需要优先验证的三项，不能等到界面完成后才发现不可用。

开发分为五个里程碑。每个里程碑都必须有可运行产物和明确验收标准。

## 2. 建议的解决方案结构

```text
SubtitleTranslator.sln
src/
  SubtitleTranslator.App/              WPF、View、ViewModel、启动与DI
  SubtitleTranslator.Application/      用例、任务编排、DTO、验证
  SubtitleTranslator.Domain/           实体、值对象、领域规则和接口
  SubtitleTranslator.Infrastructure/   SQLite、文件、密钥、日志
  SubtitleTranslator.Media/            FFmpeg、FFprobe、播放器适配
  SubtitleTranslator.Speech/           Whisper.net、模型管理、识别适配
  SubtitleTranslator.Translation/      翻译供应商、批处理、术语和缓存
  SubtitleTranslator.Subtitles/        分段、质检、SRT/ASS/VTT
tests/
  SubtitleTranslator.Domain.Tests/
  SubtitleTranslator.Subtitles.Tests/
  SubtitleTranslator.IntegrationTests/
tools/
  benchmark/                            硬件与模型基准工具
docs/
  technical-baseline.md
  development-plan.md
  adr/                                  后续架构决策记录
samples/                                不纳入版本控制的本地测试素材说明
```

依赖方向：

```text
App -> Application -> Domain
Infrastructure/Media/Speech/Translation/Subtitles -> Domain/Application contracts
```

Domain 不引用 WPF、FFmpeg、Whisper、数据库或具体云端 SDK。

## 3. 里程碑 0：技术验证与基准测试

目标：证明现有机器能够稳定完成本地识别，并确定默认模型。

任务：

1. 建立 .NET 解决方案和测试项目。
2. 编写控制台基准工具，不先开发完整 WPF。
3. 通过 FFprobe 读取视频和音轨信息。
4. 用 FFmpeg 提取 16 kHz mono WAV。
5. 集成 Whisper.net CUDA runtime。
6. 对 small、medium、large-v3-turbo 候选量化模型分别测试。
7. 记录模型大小、峰值显存、加载时间、30 分钟音频处理时间和输出质量。
8. 验证 GPU 失败后的 CPU 回退。
9. 生成最简 SRT，用播放器人工检查时间偏移。

样片集合至少覆盖：

- 清晰英语对白。
- 日语或韩语对白。
- 强背景音乐。
- 多人快速对话。
- 低声或带口音对白。
- 至少一个 30 分钟连续片段。

验收标准：

- GTX 1660 SUPER 6 GB 上至少一个均衡/高质量模型稳定运行。
- 30 分钟样片无显存溢出。
- 识别速度和显存数据被写入基准报告。
- 生成的 SRT 可被常见播放器加载且无系统性时间漂移。

交付物：可重复运行的 benchmark CLI、基准报告、默认模型 ADR。

## 4. 里程碑 1：端到端命令行 MVP

目标：无 GUI 完成一次完整字幕任务，验证领域模型和缓存边界。

任务：

1. 定义 `SubtitleProject`、`TranscriptSegment`、`TranslationSegment`、`SubtitleCue`。
2. 实现媒体探测和音频提取。
3. 实现本地转录及中间结果 JSON。
4. 实现可替换的 `ITranslationProvider`。
5. 定义结构化翻译请求，使用 SegmentId 校验响应。
6. 实现上下文窗口和术语表注入。
7. 实现基础字幕分段和阅读速度检查。
8. 输出中文 SRT、原文 SRT、双语 SRT/ASS。
9. 实现阶段缓存、幂等重试和取消。
10. 对关键规则建立单元测试和金样字幕测试。

验收标准：

- 一条命令输入视频即可产生三类字幕。
- 翻译失败不会破坏原文转录和时间戳。
- 同配置重复执行能复用识别与翻译缓存。
- SRT/ASS 时间合法、UTF-8 编码、无条目序号错误。
- 翻译响应出现漏段、重段或未知 SegmentId 时会拒绝写入并重试或报错。

## 5. 里程碑 2：WPF 桌面 MVP

目标：让普通用户无需命令行完成导入、处理、预览和导出。

页面与功能：

### 5.1 项目首页

- 拖放或选择视频。
- 最近项目。
- 新建、打开和删除项目记录；删除工程不得默认删除源视频。

### 5.2 导入设置

- 展示视频时长、分辨率、音轨和已有字幕轨。
- 音轨试听和选择。
- 源语言自动/手动选择。
- 输出中文/原文/双语选择。
- 快速/均衡/高质量识别档位。
- 翻译供应商、风格和术语表选择。
- 开始前显示预计时间、模型下载量和云端费用提示。

### 5.3 任务进度

- 展示提取、识别、翻译、质检和导出阶段。
- 支持取消、重试和恢复。
- 错误信息面向用户，技术详情写日志并可复制。

### 5.4 字幕编辑器

- 视频播放、暂停、跳转和倍速。
- 字幕列表与当前播放位置同步。
- 原文、译文、开始时间和结束时间编辑。
- 合并、拆分、前移、后移。
- 未翻译、低置信、高阅读速度和时间冲突筛选。
- 手动修改标记，重新翻译时默认保护人工修改。

### 5.5 导出

- 中文 SRT。
- 原文 SRT。
- 双语 SRT。
- 双语 ASS，并提供基础样式预设。
- 导出前运行字幕完整性检查。

验收标准：

- 新用户能从拖入视频到导出字幕完成完整流程。
- UI 长任务不阻塞，所有后台任务可取消。
- 关闭应用再打开后可以恢复未完成项目。
- 视频定位与字幕列表同步误差在用户可接受范围内，并记录实测值。

## 6. 里程碑 3：时间轴与翻译质量增强

目标：从“能生成字幕”提升到“显著减少人工修订”。

任务：

- 评估并加入 VAD。
- 加入词级时间戳或 forced alignment。
- 优化断句：停顿、标点、语义和阅读速度联合决策。
- 支持全局时间偏移和局部吸附。
- 项目人物表、术语库和固定译法。
- 跨片段上下文和批量翻译。
- 翻译记忆与相同文本复用。
- 低置信字幕自动进入复核队列。
- 可选说话人识别。
- 可选音效标记，如音乐、掌声和门响。

验收方法：

- 建立固定样片和人工参考字幕。
- 测量时间边界误差、识别字错率代理指标、漏句数、阅读速度违规数。
- 对比增强前后的人工修改次数和校订时长。

## 7. 里程碑 4：批量与产品化

任务：

- 多视频任务队列和整季电视剧工程。
- 跨集人物表、术语和翻译记忆。
- 模型下载、校验、切换和删除。
- GPU/CPU 环境诊断页。
- FFmpeg、原生运行库和模型版本清单。
- 自动更新或明确的离线升级机制。
- 安装包、卸载和用户数据保留策略。
- 崩溃报告选择加入，默认日志脱敏。
- 成本统计和预算上限。
- 完整键盘操作、快捷键和可访问性检查。

验收标准：

- 新安装的干净 Windows 环境无需开发工具即可运行。
- 用户不需要安装 Python 或 CUDA Toolkit。
- 批量任务中的单个失败不会阻断其他任务。
- 升级应用不破坏现有工程和人工修改。

## 8. 第一批核心接口草案

```csharp
public interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string mediaPath, CancellationToken cancellationToken);
}

public interface IAudioExtractor
{
    Task<AudioArtifact> ExtractAsync(
        string mediaPath,
        AudioTrackId track,
        string outputPath,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ITranscriptionEngine
{
    Task<TranscriptionResult> TranscribeAsync(
        AudioArtifact audio,
        TranscriptionOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ITranslationProvider
{
    Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
        TranslationBatch batch,
        TranslationContext context,
        CancellationToken cancellationToken);
}

public interface ISubtitleSegmenter
{
    IReadOnlyList<SubtitleCue> BuildCues(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<TranslationSegment> translations,
        SubtitleLayoutOptions options);
}
```

所有长任务接口都接受 `CancellationToken`；需要展示进度的接口使用统一进度模型；业务错误使用明确结果或异常类型，不解析字符串判断。

## 9. 数据和缓存设计

工程目录建议：

```text
ProjectName/
  project.json                 可移植元数据和版本号
  project.db                   字幕、翻译、编辑历史和状态
  cache/
    media-probe.json
    audio.wav                  可配置自动清理
    transcript.json
    translations/
  exports/
  logs/
```

缓存键至少包含：

- 输入媒体指纹和音轨。
- 音频提取参数。
- 识别引擎、模型和参数。
- 翻译供应商、模型、提示版本、术语表版本和输入文本摘要。
- 字幕分段规则版本。

任一关键参数变化时只使相关下游缓存失效，不重新执行无关阶段。

## 10. 测试计划

### 单元测试

- 时间码解析和格式化。
- 字幕重叠检测。
- 阅读速度计算。
- 分段、合并和换行。
- SegmentId 映射和翻译响应校验。
- SRT/ASS 转义、编码和序号。
- 缓存键与失效规则。

### 集成测试

- FFprobe 元数据解析。
- FFmpeg 音轨提取、取消和错误处理。
- Whisper 模型加载、CUDA 执行和 CPU 回退。
- SQLite 迁移与工程恢复。
- 翻译服务模拟服务器、限流、超时和重试。

### 端到端测试

- 短视频完整流水线。
- 应用中途关闭和恢复。
- 无网络、错误密钥、额度不足。
- 路径含中文、空格和长文件名。
- 多音轨、多字幕轨和可变帧率视频。

## 11. 近期实施顺序

接下来建议按以下顺序执行：

1. 创建解决方案、项目骨架和依赖边界测试。
2. 创建 benchmark CLI。
3. 集成 FFprobe/FFmpeg，验证中文路径和取消。
4. 集成 Whisper.net CUDA。
5. 下载候选模型并跑实际 GPU 基准。
6. 根据结果确定默认模型和降级策略。
7. 实现领域模型、SRT 导出和金样测试。
8. 接入第一个翻译 Provider。
9. 完成命令行端到端 MVP。
10. 再开始 WPF 主界面和播放器。

在步骤 5 完成前，不冻结模型选择；在步骤 9 通过前，不投入大量时间美化 WPF 界面。

## 12. 首个开发迭代的完成定义

首个迭代聚焦里程碑 0，完成条件如下：

- 仓库可以用一条 `dotnet build` 命令构建。
- benchmark CLI 能读取视频、选择音轨、提取音频并调用 GPU Whisper。
- 用户可以选择 small/medium/large-v3-turbo 候选模型。
- 输出原文 JSON 和 SRT。
- 输出耗时、实时倍率、峰值或可观测显存数据及错误日志。
- 至少完成三种候选模型的同一样片对比。
- 形成一份默认模型和发布运行库的 ADR。

