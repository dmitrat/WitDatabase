# Design tokens

Everything that decides how Studio looks lives here as a **named token**, defined once and referenced
everywhere. A colour, a font size or a control height written into a view is a defect in this folder's
terms, and there are tests that count them.

```
Tokens.axaml       Colour roles, in both theme variants
Typography.axaml   The type scale, named by purpose
Metrics.axaml      Heights, radii, panel widths, icon sizes and stroke
Controls.axaml     Control styles built from all three
SqlEditorTheme.cs  The SQL editor's colours, which come from App.axaml
```

`App.axaml` merges the first three as resource dictionaries and includes `Controls.axaml` as a style.

## Tokens.axaml - colour

34 roles, each defined twice - once under `Dark` and once under `Light` in a `ThemeDictionaries` -
and each published in two forms: a `Color` (`Wit.Color.Surface.Panel`) for anything that needs to
compose one, and a `SolidColorBrush` (`Wit.Surface.Panel`) for everything else. 68 brushes in total,
because the connection palette is per-connection rather than per-theme.

The rule that is easy to get wrong: **the accent splits.** `Wit.Accent` is `#4CC13C` in both themes -
it is the brand's green and a fill does not need to change - while `Wit.Accent.Text` is `#4CC13C` on
dark and `#2E7D22` on light, because the light value is what makes text on a pale surface readable.

## Typography.axaml - the type scale

Nine steps, named for what they are for rather than how big they are: `Wit.Font.WindowTitle`,
`.SectionTitle`, `.BlockTitle`, `.Body`, `.Control`, `.Caption`, and three monospaced -
`.Mono.Label` (9.5), `.Mono.Data` (11), `.Mono.Sql` (13).

## Metrics.axaml - the measurements

Row and control heights (`Wit.Height.Toolbar`, `.Tab`, `.TreeRow`, `.GridRow`, `.Field`, `.Button`,
`.ButtonCompact`, `.Chip`, `.StatusBar`, `.TitleBar`, `.DesignerRow`), six corner radii
(`Wit.Radius.Window`, `.Block`, `.Field`, `.Button`, `.Chip`, `.Check`), four icon sizes and
`Wit.Icon.Stroke` = 1.7, which is the outline weight every icon is drawn with.

## Controls.axaml - the styles

Where the tokens meet Fluent. Two things in it are worth knowing before editing:

- **Every reference is `{DynamicResource}`, never `{StaticResource}`.** Avalonia populates
  `Application.Styles` *before* `Application.Resources`, so a `StaticResource` in a control style
  throws while the application is initialising - it compiles, it ships, and Studio does not start.
  That happened; `DesignTokenTests` now catches it.
- **A value written in a `ControlTemplate` is a local value and beats a style setter.** Restyling
  `PathIcon /template/ Path` cannot remove the `Fill` Fluent writes there, so the template is
  *replaced* rather than restyled. The replacement draws into a fixed 24x24 `Canvas`, because a
  `Viewbox` scales its child to the **child's** bounds - without the canvas each icon gets its own
  scale and the large ones look cropped.

Fluent's own chevrons (combo box, submenu, spinner, calendar) are filled glyphs, so those keep a
filled template: the outline treatment is for Studio's icons, not for the framework's.

## SQL editor colours

The editor's background, foreground and line numbers come from `App.axaml`'s theme dictionaries under
the keys `SqlEditorBg`, `SqlEditorFg` and `SqlEditorLineNumbers`; `SqlEditorTheme.cs` reads them for
the current variant and falls back to compiled-in defaults if they are missing.

Syntax highlighting is separate, in `Syntax/`: `WitSql.xshd` for dark and `WitSqlLight.xshd` for
light, selected by `WitSqlHighlighting` from the active variant. New keywords have to be added to
**both** files.

## What guards this folder

`Tools/OutWit.Database.Studio.Tests/Themes/DesignTokenTests.cs`, in a headless Avalonia application:

- every colour token resolves in **both** variants, and both variants define the same keys;
- the type scale and the metric table match the design canon, value by value;
- every token the markup asks for exists - the check that catches a renamed token;
- the palette handed to Fluent is the canon palette;
- a census of hard-coded values in `Views/`: hex colours, `FontSize=`, `Height=`. The numbers are
  asserted, so adding one fails the build rather than quietly starting a drift back.
