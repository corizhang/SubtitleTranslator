using SubtitleTranslator.Application;

namespace SubtitleTranslator.App;

public static class ModelCatalog
{
    private static readonly Uri BaseUri = new("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/");

    public static DownloadableModel Small { get; } = new(
        "small-q5-1", "Small Q5_1", "ggml-small-q5_1.bin", 190_085_487,
        "AE85E4A935D7A567BD102FE55AFC16BB595BDB618E11B2FC7591BC08120411BB",
        new Uri(BaseUri, "ggml-small-q5_1.bin"), "速度优先，约 181 MiB");

    public static DownloadableModel Turbo { get; } = new(
        "large-v3-turbo-q5-0", "Large v3 Turbo Q5_0", "ggml-large-v3-turbo-q5_0.bin", 574_041_195,
        "394221709CD5AD1F40C46E6031CA61BCE88931E6E088C188294C6D5A55FFA7E2",
        new Uri(BaseUri, "ggml-large-v3-turbo-q5_0.bin"), "推荐质量，约 547 MiB");
}
