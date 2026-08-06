using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// A column as the designer holds it while it is being edited - the editable twin of
/// <see cref="ColumnInfo"/>, which is what the catalogue said.
///
/// It keeps the original beside the current values, because every question the designer asks is a
/// comparison: has the type changed (a rebuild, WS-40), has only the default changed (in place), is
/// this a column that did not exist a minute ago.
///
/// Hand-written notification rather than the [Notify] aspect, which is what the other bindable models
/// here do (<see cref="ColumnFilter"/>): these are bound two ways from a grid.
/// </summary>
public sealed class ColumnDraft : INotifyPropertyChanged
{
    #region Fields

    private string m_name = string.Empty;
    private string m_dataType = "VARCHAR";
    private int? m_maxLength;
    private int? m_numericPrecision;
    private int? m_numericScale;
    private bool m_isNullable = true;
    private string? m_defaultValue;
    private bool m_isPrimaryKey;
    private bool m_isAutoIncrement;
    private bool m_isUnique;
    private string? m_checkExpression;
    private string? m_computedExpression;
    private string? m_referencesTable;
    private string? m_referencesColumn;
    private bool m_isDeleted;
    private string? m_marker;
    private string? m_markerReason;

    #endregion

    #region Constructors

    public ColumnDraft()
    {
    }

    public ColumnDraft(ColumnInfo column)
    {
        Original = column;

        m_name = column.Name;
        m_dataType = column.DataType;
        m_maxLength = column.MaxLength;
        m_numericPrecision = column.NumericPrecision;
        m_numericScale = column.NumericScale;
        m_isNullable = column.IsNullable;
        m_defaultValue = column.DefaultValue;
        m_isPrimaryKey = column.IsPrimaryKey;
        m_isAutoIncrement = column.IsAutoIncrement;
        m_isUnique = column.IsUnique;
        m_checkExpression = column.CheckExpression;
        m_computedExpression = column.GenerationExpression;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The type as it goes into DDL: VARCHAR(32), DECIMAL(18,2), INTEGER.
    /// </summary>
    public static string FormatType(string dataType, int? maxLength, int? precision, int? scale)
    {
        if (string.IsNullOrWhiteSpace(dataType))
            return string.Empty;

        if (maxLength is { } length && length > 0 && TakesLength(dataType))
            return $"{dataType}({length})";

        if (precision is { } p && p > 0 && TakesPrecision(dataType))
            return scale is { } s ? $"{dataType}({p},{s})" : $"{dataType}({p})";

        return dataType;
    }

    public static bool TakesLength(string dataType) =>
        dataType.Equals("VARCHAR", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("CHAR", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("NVARCHAR", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("NCHAR", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("VARBINARY", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("BINARY", StringComparison.OrdinalIgnoreCase);

    public static bool TakesPrecision(string dataType) =>
        dataType.Equals("DECIMAL", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("NUMERIC", StringComparison.OrdinalIgnoreCase);

    private static string? NormalisedDefault(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    #endregion

    #region Properties

    /// <summary>
    /// What the catalogue said before anything was typed. Null for a column being added.
    /// </summary>
    public ColumnInfo? Original { get; }

    public bool IsNew => Original == null;

    public string TypeText => FormatType(DataType, MaxLength, NumericPrecision, NumericScale);

    public string OriginalTypeText => Original == null
        ? string.Empty
        : FormatType(Original.DataType, Original.MaxLength, Original.NumericPrecision, Original.NumericScale);

    /// <summary>
    /// True when the type declaration differs from the one the catalogue reported. This single
    /// question is what turns an edit into a rebuild (WS-40).
    /// </summary>
    public bool TypeChanged => Original != null &&
                              !string.Equals(TypeText, OriginalTypeText, StringComparison.OrdinalIgnoreCase);

    public bool DefaultChanged => Original != null && !string.Equals(
        NormalisedDefault(DefaultValue), NormalisedDefault(Original.DefaultValue), StringComparison.Ordinal);

    public bool NullabilityChanged => Original != null && IsNullable != Original.IsNullable;

    public bool NameChanged => Original != null && !string.Equals(Name, Original.Name, StringComparison.Ordinal);

    public bool KeyChanged => Original != null && IsPrimaryKey != Original.IsPrimaryKey;

    public bool IsComputed => !string.IsNullOrWhiteSpace(ComputedExpression);

    public string Name
    {
        get => m_name;
        set => Set(ref m_name, value);
    }

    public string DataType
    {
        get => m_dataType;
        set { if (Set(ref m_dataType, value)) RaiseType(); }
    }

    public int? MaxLength
    {
        get => m_maxLength;
        set { if (Set(ref m_maxLength, value)) RaiseType(); }
    }

    public int? NumericPrecision
    {
        get => m_numericPrecision;
        set { if (Set(ref m_numericPrecision, value)) RaiseType(); }
    }

    public int? NumericScale
    {
        get => m_numericScale;
        set { if (Set(ref m_numericScale, value)) RaiseType(); }
    }

    public bool IsNullable
    {
        get => m_isNullable;
        set => Set(ref m_isNullable, value);
    }

    public string? DefaultValue
    {
        get => m_defaultValue;
        set => Set(ref m_defaultValue, value);
    }

    public bool IsPrimaryKey
    {
        get => m_isPrimaryKey;
        set => Set(ref m_isPrimaryKey, value);
    }

    public bool IsAutoIncrement
    {
        get => m_isAutoIncrement;
        set => Set(ref m_isAutoIncrement, value);
    }

    public bool IsUnique
    {
        get => m_isUnique;
        set => Set(ref m_isUnique, value);
    }

    public string? CheckExpression
    {
        get => m_checkExpression;
        set => Set(ref m_checkExpression, value);
    }

    /// <summary>
    /// A computed column's expression. Kept apart from a default: a computed column takes no value on
    /// INSERT at all - measured 2026-08-06, naming one in the column list of an INSERT is refused -
    /// which is what a rebuild's copy step has to know.
    /// </summary>
    public string? ComputedExpression
    {
        get => m_computedExpression;
        set { if (Set(ref m_computedExpression, value)) Raise(nameof(IsComputed)); }
    }

    public string? ReferencesTable
    {
        get => m_referencesTable;
        set => Set(ref m_referencesTable, value);
    }

    public string? ReferencesColumn
    {
        get => m_referencesColumn;
        set => Set(ref m_referencesColumn, value);
    }

    /// <summary>
    /// Marked for removal. The row stays visible until Apply, so the grid and the DDL panel agree
    /// about what is pending.
    /// </summary>
    public bool IsDeleted
    {
        get => m_isDeleted;
        set => Set(ref m_isDeleted, value);
    }

    /// <summary>
    /// The category of the pending edit on this row - "in place", "rebuild", "drop + create" - or null
    /// when nothing is pending on it.
    ///
    /// It lives on the row because that is where WS-39 puts it: the category is visible as soon as the
    /// edit is made, not after Apply has been pressed and the answer has come back.
    /// </summary>
    public string? Marker
    {
        get => m_marker;
        set { if (Set(ref m_marker, value)) Raise(nameof(HasMarker)); }
    }

    public bool HasMarker => !string.IsNullOrEmpty(Marker);

    /// <summary>
    /// Why the marker says what it says. A bare icon is a rule nobody can check.
    /// </summary>
    public string? MarkerReason
    {
        get => m_markerReason;
        set => Set(ref m_markerReason, value);
    }

    #endregion

    #region Notification

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(property);

        return true;
    }

    private void RaiseType()
    {
        Raise(nameof(TypeText));
        Raise(nameof(TypeChanged));
    }

    private void Raise([CallerMemberName] string? property = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    #endregion
}
