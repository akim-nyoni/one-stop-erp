using One4All.Desktop.Navigation;

namespace One4All.Desktop.ViewModels;

/// <summary>A single module tile in the home grid / System Tree.</summary>
public sealed class ModuleTileViewModel
{
    /// <summary>Creates the tile from a descriptor.</summary>
    /// <param name="descriptor">The module descriptor.</param>
    public ModuleTileViewModel(ModuleDescriptor descriptor) => Descriptor = descriptor;

    /// <summary>The underlying descriptor.</summary>
    public ModuleDescriptor Descriptor { get; }

    /// <summary>The display title.</summary>
    public string Title => Descriptor.Title;

    /// <summary>The tile glyph.</summary>
    public string Glyph => Descriptor.Glyph;

    /// <summary>The accent colour.</summary>
    public string Accent => Descriptor.Accent;

    /// <summary>The standard child groups under this module.</summary>
    public IReadOnlyList<string> Groups => ModuleDescriptor.ChildGroups;
}
