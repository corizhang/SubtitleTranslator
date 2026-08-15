using SubtitleTranslator.Speech;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: SubtitleTranslator.RuntimeProbe <model.bin> <runtime-directory>");
    return 2;
}

try
{
    WhisperNativeRuntimeBootstrap.Configure(args[1]);
    Console.WriteLine($"Configured library: {Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath}");
    var result = await new WhisperRuntimeSelfTestService().RunAsync(args[0], args[1], null, CancellationToken.None);
    Console.WriteLine($"OK: {result.Message} ({result.Elapsed.TotalSeconds:0.0}s)");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
