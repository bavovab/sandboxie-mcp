namespace SandboxieMcp.Core.Services;

public sealed class TemplateManager(SandboxManager boxes)
{
    public object List()
    {
        boxes.Guard.Demand("global_config"); boxes.Guard.Demand("config_read");
        return boxes.Api.Query(null, null).Where(x => x.StartsWith("Template_", StringComparison.OrdinalIgnoreCase))
            .Select(x => new { section = x, name = x[9..], title = boxes.Api.Query(x, "Tmpl.Title").FirstOrDefault() }).ToArray();
    }

    public Task<object> Assign(string sandbox, string[] templates, string revision, CancellationToken token)
    {
        _ = boxes.RequireBox(sandbox, "config_write"); boxes.Guard.Demand("global_config");
        if (templates is null || templates.Length > 128 || templates.Distinct(StringComparer.OrdinalIgnoreCase).Count() != templates.Length)
            throw new ArgumentException("Supply at most 128 distinct templates.");
        foreach (var template in templates)
        {
            SandboxManager.ValidateSection("Template_" + template);
            if (boxes.Api.Query("Template_" + template, null).Length == 0)
                throw new ArgumentException("A requested template does not exist.");
        }
        return boxes.Apply(sandbox, [new("Template", templates)], revision, token);
    }

    public async Task<object> Delete(string template, CancellationToken token)
    {
        var section = "Template_" + template;
        SandboxManager.ValidateSection(section); boxes.Guard.DemandSection("config_write", section);
        await boxes.MutationGate.WaitAsync(token);
        try
        {
            var builtIn = Path.Combine(boxes.Api.InstallationPath, "Templates.ini");
            if (File.Exists(builtIn) && File.ReadLines(builtIn).Any(x => x.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase)))
                throw new SandboxieException("access_denied", "Built-in templates cannot be deleted.");
            if (boxes.Api.Query(section, null).Length == 0) throw new SandboxieException("not_found", "The template does not exist.");
            if (boxes.Api.GetSandboxes().Concat(boxes.Api.Query(null, null).Where(x => x.StartsWith("Template_", StringComparison.OrdinalIgnoreCase)))
                .Append("GlobalSettings").Any(x => boxes.Api.Query(x, "Template", true).Contains(template, StringComparer.OrdinalIgnoreCase)))
                throw new SandboxieException("access_denied", "The template is referenced by another section.");
            var backup = boxes.Backup(section, boxes.ReadRaw(section));
            boxes.Api.Update(section, "*", null); boxes.Api.Reload();
            if (boxes.Api.Query(section, null).Length != 0) throw new SandboxieException("verification_failed", "The template section still exists.");
            return new { deleted = true, template, backup };
        }
        finally { boxes.MutationGate.Release(); }
    }
}
