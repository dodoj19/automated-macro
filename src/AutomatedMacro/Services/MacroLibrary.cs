using System.Collections.ObjectModel;
using System.IO;

namespace AutomatedMacro;

public enum LibraryKind { Folder, File, Trash, TrashedFile }

/// <summary>Stavka u popisu makroa (lijevi stupac): mapa, datoteka ili kos.</summary>
public sealed class LibraryItem : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isSelected;
    private bool _isCurrent;

    public LibraryItem(LibraryKind kind, string name, string fullPath)
    {
        Kind = kind;
        Name = name;
        FullPath = fullPath;
    }

    public LibraryKind Kind { get; }
    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<LibraryItem> Children { get; } = new();

    public bool IsFolder => Kind is LibraryKind.Folder or LibraryKind.Trash;
    public bool IsFile => Kind == LibraryKind.File;
    public bool IsTrashed => Kind == LibraryKind.TrashedFile;

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Ova datoteka je trenutno otvorena.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (Set(ref _isCurrent, value)) { OnPropertyChanged(nameof(Glyph)); OnPropertyChanged(nameof(GlyphColor)); } }
    }

    public string Glyph => Kind switch
    {
        LibraryKind.Folder => "",
        LibraryKind.Trash => "",
        _ => IsCurrent ? "" : "",
    };

    public string GlyphColor => Kind switch
    {
        LibraryKind.Folder => "#F5A524",
        LibraryKind.Trash => "#A0A0AB",
        LibraryKind.TrashedFile => "#6B6B76",
        _ => IsCurrent ? "#B4A5FF" : "#A0A0AB",
    };

    public IEnumerable<LibraryItem> Descendants()
    {
        foreach (var c in Children)
        {
            yield return c;
            foreach (var d in c.Descendants()) yield return d;
        }
    }
}

/// <summary>Mapa s makroima. Obrisane procedure idu u skrivenu mapu ".kos".</summary>
public static class MacroLibrary
{
    public const string TrashFolderName = ".kos";

    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "Makroi");

    public static string TrashPath(string root) => Path.Combine(root, TrashFolderName);

    public static List<LibraryItem> Build(string root, string? filter)
    {
        Directory.CreateDirectory(root);
        filter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

        // Zadana mapa "Makroi" prikazuje se prevedenim imenom; vlastita mapa svojim imenom.
        bool isDefault = string.Equals(Path.GetFullPath(root).TrimEnd('\\'), Path.GetFullPath(DefaultRoot).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
        var rootName = isDefault ? Loc.T("LibraryRoot") : Path.GetFileName(root.TrimEnd('\\', '/'));
        var rootItem = new LibraryItem(LibraryKind.Folder, rootName, root);
        Fill(rootItem, root, filter);

        var trash = new LibraryItem(LibraryKind.Trash, Loc.T("Trash"), TrashPath(root)) { IsExpanded = false };
        if (Directory.Exists(trash.FullPath))
        {
            foreach (var f in Directory.EnumerateFiles(trash.FullPath, "*" + ProcedureFile.Extension).Order(StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (filter == null || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                    trash.Children.Add(new LibraryItem(LibraryKind.TrashedFile, name, f));
            }
        }
        if (filter != null && trash.Children.Count > 0) trash.IsExpanded = true;

        return [rootItem, trash];
    }

    private static void Fill(LibraryItem folder, string path, string? filter)
    {
        foreach (var dir in Directory.EnumerateDirectories(path).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            var sub = new LibraryItem(LibraryKind.Folder, name, dir);
            Fill(sub, dir, filter);
            bool nameMatch = filter != null && name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
            if (filter == null || nameMatch || sub.Children.Count > 0) folder.Children.Add(sub);
        }

        foreach (var file in Directory.EnumerateFiles(path, "*" + ProcedureFile.Extension).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (filter == null || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                folder.Children.Add(new LibraryItem(LibraryKind.File, name, file));
        }
    }

    public static string UniqueFilePath(string folder, string baseName)
    {
        baseName = SafeName(baseName);
        var path = Path.Combine(folder, baseName + ProcedureFile.Extension);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{baseName} ({i}){ProcedureFile.Extension}");
        return path;
    }

    public static string UniqueFolderPath(string parent, string baseName)
    {
        baseName = SafeName(baseName);
        var path = Path.Combine(parent, baseName);
        for (int i = 2; Directory.Exists(path); i++)
            path = Path.Combine(parent, $"{baseName} ({i})");
        return path;
    }

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return string.IsNullOrEmpty(cleaned) ? Loc.T("Kind_Procedure") : cleaned;
    }

    public static string MoveToTrash(string root, string file)
    {
        var trash = TrashPath(root);
        Directory.CreateDirectory(trash);
        File.SetAttributes(trash, File.GetAttributes(trash) | FileAttributes.Hidden);
        var target = UniqueFilePath(trash, Path.GetFileNameWithoutExtension(file));
        File.Move(file, target);
        return target;
    }

    public static string Restore(string root, string trashedFile)
    {
        var target = UniqueFilePath(root, Path.GetFileNameWithoutExtension(trashedFile));
        File.Move(trashedFile, target);
        return target;
    }
}
