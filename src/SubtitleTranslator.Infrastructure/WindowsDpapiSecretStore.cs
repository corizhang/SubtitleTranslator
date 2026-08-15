using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class WindowsDpapiSecretStore(string directory) : ISecretStore
{
    public async Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        var path = GetPath(name);
        if (!File.Exists(path)) return null;
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        return Encoding.UTF8.GetString(Unprotect(encrypted));
    }

    public async Task WriteAsync(string name, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("密钥不能为空。", nameof(value));
        Directory.CreateDirectory(directory);
        var path = GetPath(name);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, Protect(Encoding.UTF8.GetBytes(value.Trim())), cancellationToken);
        File.Move(temporary, path, true);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(name);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string name)
    {
        if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("密钥名称无效。", nameof(name));
        return Path.Combine(directory, name + ".bin");
    }

    private static byte[] Protect(byte[] input) => Transform(input, protect: true);
    private static byte[] Unprotect(byte[] input) => Transform(input, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI 仅支持 Windows。");
        var inputBlob = new DataBlob();
        var outputBlob = new DataBlob();
        try
        {
            inputBlob.Size = input.Length;
            inputBlob.Data = Marshal.AllocHGlobal(input.Length);
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);
            var success = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out outputBlob);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var output = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            if (inputBlob.Data != IntPtr.Zero) Marshal.FreeHGlobal(inputBlob.Data);
            if (outputBlob.Data != IntPtr.Zero) LocalFree(outputBlob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
