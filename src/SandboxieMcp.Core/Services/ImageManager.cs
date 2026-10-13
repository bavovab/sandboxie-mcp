namespace SandboxieMcp.Core.Services;

public sealed class ImageManager(SandboxManager boxes)
{
    public object Query(string sandbox)
    {
        sandbox = boxes.RequireBox(sandbox, "image_read");
        return boxes.Api.QueryImage(sandbox);
    }

    public async Task<object> Create(string sandbox, long sizeMiB, string? secretEnvironmentVariable, CancellationToken token)
    {
        sandbox = boxes.RequireBox(sandbox, "image_write");
        if (sizeMiB is < 64 or > 1_048_576) throw new ArgumentException("sizeMiB must be 64..1048576.");
        RequireFeature();
        await boxes.MutationGate.WaitAsync(token);
        try
        {
            boxes.RequireStopped(sandbox);
            var root = boxes.Paths(sandbox, "image_write").FileRoot;
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new SandboxieException("not_empty", "Create an image only for an empty sandbox.");
            boxes.Api.CreateImage(sandbox, checked(sizeMiB * 1024), Secret(secretEnvironmentVariable));
            return new { created = true, sandbox, sizeMiB, serviceAccepted = true };
        }
        finally { boxes.MutationGate.Release(); }
    }

    public async Task<object> Mount(string sandbox, string? secretEnvironmentVariable, bool protect, CancellationToken token)
    {
        sandbox = boxes.RequireBox(sandbox, "image_write");
        RequireFeature();
        await boxes.MutationGate.WaitAsync(token);
        try
        {
            boxes.RequireStopped(sandbox);
            boxes.Api.Mount(sandbox, Secret(secretEnvironmentVariable), protect);
            return new { mounted = true, sandbox, image = boxes.Api.QueryImage(sandbox) };
        }
        finally { boxes.MutationGate.Release(); }
    }

    public async Task<object> Unmount(string sandbox, CancellationToken token)
    {
        sandbox = boxes.RequireBox(sandbox, "image_write");
        await boxes.MutationGate.WaitAsync(token);
        try { boxes.RequireStopped(sandbox); boxes.Api.Unmount(sandbox); return new { unmounted = true, sandbox }; }
        finally { boxes.MutationGate.Release(); }
    }

    private void RequireFeature()
    {
        var flags = boxes.Api.Features;
        if ((flags & 0x80000080u) != 0x80000080u)
            throw new SandboxieException("feature_unavailable", "Image operations require an active encryption entitlement and the Sandboxie image components.");
    }

    private string? Secret(string? variable)
    {
        if (variable is null) return null;
        if (!boxes.Configuration.SecretEnvironmentVariables.Contains(variable, StringComparer.Ordinal))
            throw new SandboxieException("access_denied", "The secret environment variable is not configured by the user.");
        var value = Environment.GetEnvironmentVariable(variable);
        if (value is null || value.Length > 128) throw new ArgumentException("The configured secret is missing or exceeds 128 characters.");
        return value;
    }
}
