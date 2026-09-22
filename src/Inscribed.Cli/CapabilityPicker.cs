using Inscribed.Auth.Authorization;
using Inscribed.Cli.Ui;

namespace Inscribed.Cli;

internal static class CapabilityPicker
{
    public static string Pick(Screen screen, Block block, GrantTarget target, bool required)
    {
        var presets = CapabilityCatalog.Presets.Select(preset => new MenuItem(
            preset.Key,
            preset.Key,
            string.Join(Output.Dim(" + "), preset.Value.Select(Output.Capability)),
            "PRESETS",
            Refusal(preset.Value, target)));

        var individual = CapabilityCatalog.All
            .Except(CapabilityCatalog.InstallationWide, StringComparer.Ordinal)
            .Select(capability => new MenuItem(capability, capability, Group: "CAPABILITIES", Disabled: Refusal([capability], target)));

        var title = required ? "capabilities" : $"capabilities {Output.Dim("(optional)")}";
        var picked = new SelectMenu(title, [.. presets, .. individual], multiple: true, block.Prefix, Block.Indent).Run(screen)
            ?? throw new PromptCancelledException();

        block.Line($"capabilities: {string.Join(", ", picked.Select(item => item.Label))}");
        return string.Join(',', picked.Select(item => item.Value));
    }

    private static string? Refusal(IEnumerable<string> capabilities, GrantTarget target) =>
        target is GrantTarget.ServiceKey && capabilities.Intersect(CapabilityCatalog.HumanOnly, StringComparer.Ordinal).Any()
            ? "human only"
            : null;
}
