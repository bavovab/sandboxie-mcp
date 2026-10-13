namespace SandboxieMcp.Core.Services;

internal static class BoundedCopy
{
    public static async Task<long> Copy(Stream input, Stream output, long limit, CancellationToken token)
    {
        var buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == 0) return total;
            if (read > limit - total) throw new ArgumentException("Decompressed data exceeds its declared size or configured limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            total += read;
        }
    }
}
