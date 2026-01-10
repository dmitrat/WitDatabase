# SQL Editor Theme Support

This folder contains the theme infrastructure for the SQL Editor component.

## Architecture

```
SqlEditorColors.axaml          ? Editor background/foreground colors
SqlEditorTheme.cs              ? Runtime access to editor colors
WitSql.xshd (Syntax folder)    ? Dark theme syntax highlighting
WitSqlLight.xshd (Syntax folder) ? Light theme syntax highlighting
```

## Files

### SqlEditorColors.axaml
Default (light theme) colors for the SQL Editor background and foreground.
These can be overridden by theme-specific resource dictionaries.

### SqlEditorTheme.cs
Static helper class that provides access to editor colors.
- Detects current theme (light/dark)
- Returns appropriate default colors based on theme
- Can read colors from application resources

### Syntax/WitSql.xshd
Dark theme syntax highlighting (VS Code Dark+ style).
Contains all SQL keywords, data types, functions with dark theme colors.

### Syntax/WitSqlLight.xshd  
Light theme syntax highlighting (Visual Studio style).
Contains all SQL keywords, data types, functions with light theme colors.

## Usage

### In SqlEditor.cs
```csharp
// Apply theme-aware colors
Background = SqlEditorTheme.BackgroundBrush;
Foreground = SqlEditorTheme.ForegroundBrush;
LineNumbersForeground = SqlEditorTheme.LineNumbersBrush;

// Apply syntax highlighting (automatically selects light/dark based on theme)
SyntaxHighlighting = WitSqlHighlighting.Definition;
```

### Theme Change Handling
```csharp
private void OnThemeChanged(object? sender, EventArgs e)
{
    // Re-apply editor colors
    ApplyThemeColors();
    
    // Re-apply syntax highlighting (will select appropriate xshd file)
    SyntaxHighlighting = WitSqlHighlighting.CreateDefinition();
}
```

## Color Scheme Reference

### Light Theme (Visual Studio style) - WitSqlLight.xshd
| Element | Color |
|---------|-------|
| Background | #FFFFFF |
| Foreground | #1E1E1E |
| Comment | #008000 |
| String | #A31515 |
| Keyword | #0000FF |
| DataType | #2B91AF |
| Function | #795E26 |
| Number | #098658 |

### Dark Theme (VS Code Dark+ style) - WitSql.xshd
| Element | Color |
|---------|-------|
| Background | #1E1E1E |
| Foreground | #D4D4D4 |
| Comment | #6A9955 |
| String | #CE9178 |
| Keyword | #569CD6 |
| DataType | #4EC9B0 |
| Function | #DCDCAA |
| Number | #B5CEA8 |

## Adding New Keywords

To add new SQL keywords or functions, edit both xshd files:
1. `Syntax/WitSql.xshd` - Dark theme
2. `Syntax/WitSqlLight.xshd` - Light theme

Keywords are grouped by category (DML, DDL, Constraints, etc.) for maintainability.
