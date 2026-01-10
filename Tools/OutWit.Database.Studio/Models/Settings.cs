using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Common.Collections;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Application settings stored in JSON file.
/// </summary>
public sealed class Settings : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not Settings other)
            return false;

        return Theme.Is(other.Theme)
            && RecentFiles.Is(other.RecentFiles)
            && MaxRecentFiles.Is(other.MaxRecentFiles)
            && AutoSaveQueries.Is(other.AutoSaveQueries)
            && EditorFontSize.Is(other.EditorFontSize);
    }

    public override Settings Clone()
    {
        return new Settings
        {
            Theme = Theme,
            RecentFiles = RecentFiles.ToList(),
            MaxRecentFiles = MaxRecentFiles,
            AutoSaveQueries = AutoSaveQueries,
            EditorFontSize = EditorFontSize
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the application theme (Light, Dark).
    /// </summary>
    public string Theme { get; set; } = "Light";

    /// <summary>
    /// Gets or sets the list of recently opened database files.
    /// </summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of recent files to keep.
    /// </summary>
    public int MaxRecentFiles { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether to auto-save queries.
    /// </summary>
    public bool AutoSaveQueries { get; set; } = true;

    /// <summary>
    /// Gets or sets the editor font size.
    /// </summary>
    public int EditorFontSize { get; set; } = 14;

    #endregion
}
