# Material Design Icons Reference for WitDatabase Studio

Quick reference for adding Material Design icons to Avalonia UI using PathIcon.

---

## Usage Pattern

```xml
<PathIcon Data="[SVG_PATH_DATA]" 
         Width="16" 
         Height="16"/>
```

---

## Common Database Icons

### Storage & Database

**Storage (Database)**
```xml
<PathIcon Data="M2 6V8H14V6H2M2 10V12H14V10H2M20 10.1C19.9 10.1 19.7 10.2 19.6 10.3L18.6 11.3L20.7 13.4L21.7 12.4C21.9 12.2 21.9 11.8 21.7 11.6L20.4 10.3C20.3 10.2 20.2 10.1 20 10.1M18.1 11.9L12 17.9V20H14.1L20.2 13.9L18.1 11.9Z" 
         Width="16" Height="16"/>
```

**Database**
```xml
<PathIcon Data="M12,3C7.58,3 4,4.79 4,7C4,9.21 7.58,11 12,11C16.42,11 20,9.21 20,7C20,4.79 16.42,3 12,3M4,9V12C4,14.21 7.58,16 12,16C16.42,16 20,14.21 20,12V9C20,11.21 16.42,13 12,13C7.58,13 4,11.21 4,9M4,14V17C4,19.21 7.58,21 12,21C16.42,21 20,19.21 20,17V14C20,16.21 16.42,18 12,18C7.58,18 4,16.21 4,14Z" 
         Width="16" Height="16"/>
```

### Tables & Structure

**Table**
```xml
<PathIcon Data="M5,4H19A2,2 0 0,1 21,6V18A2,2 0 0,1 19,20H5A2,2 0 0,1 3,18V6A2,2 0 0,1 5,4M5,8V12H11V8H5M13,8V12H19V8H13M5,14V18H11V14H5M13,14V18H19V14H13Z" 
         Width="16" Height="16"/>
```

**Table Edit**
```xml
<PathIcon Data="M21.7 13.35L20.7 14.35L18.65 12.3L19.65 11.3C19.86 11.09 20.21 11.09 20.42 11.3L21.7 12.58C21.91 12.79 21.91 13.14 21.7 13.35M12 18.94L18.06 12.88L20.11 14.93L14.06 21H12V18.94M4 2H18A2 2 0 0 1 20 4V8.17L16.17 12H12V16.17L10.17 18H4A2 2 0 0 1 2 16V4A2 2 0 0 1 4 2M4 6V10H10V6H4M12 6V10H18V6H12M4 12V16H10V12H4Z" 
         Width="16" Height="16"/>
```

### Folders

**Folder**
```xml
<PathIcon Data="M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z" 
         Width="16" Height="16"/>
```

**Folder Open**
```xml
<PathIcon Data="M19,20H4C2.89,20 2,19.1 2,18V6C2,4.89 2.89,4 4,4H10L12,6H19A2,2 0 0,1 21,8H21L4,8V18L6.14,10H23.21L20.93,18.5C20.7,19.37 19.92,20 19,20Z" 
         Width="16" Height="16"/>
```

### Views & Visibility

**Eye (View)**
```xml
<PathIcon Data="M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5Z" 
         Width="16" Height="16"/>
```

**Eye Off (Hidden)**
```xml
<PathIcon Data="M11.83,9L15,12.16C15,12.11 15,12.05 15,12A3,3 0 0,0 12,9C11.94,9 11.89,9 11.83,9M7.53,9.8L9.08,11.35C9.03,11.56 9,11.77 9,12A3,3 0 0,0 12,15C12.22,15 12.44,14.97 12.65,14.92L14.2,16.47C13.53,16.8 12.79,17 12,17A5,5 0 0,1 7,12C7,11.21 7.2,10.47 7.53,9.8M2,4.27L4.28,6.55L4.73,7C3.08,8.3 1.78,10 1,12C2.73,16.39 7,19.5 12,19.5C13.55,19.5 15.03,19.2 16.38,18.66L16.81,19.08L19.73,22L21,20.73L3.27,3M12,7A5,5 0 0,1 17,12C17,12.64 16.87,13.26 16.64,13.82L19.57,16.75C21.07,15.5 22.27,13.86 23,12C21.27,7.61 17,4.5 12,4.5C10.6,4.5 9.26,4.75 8,5.2L10.17,7.35C10.74,7.13 11.35,7 12,7Z" 
         Width="16" Height="16"/>
```

### Actions & Operations

**Flash (Lightning) - Index**
```xml
<PathIcon Data="M7,2V13H10V22L17,10H13L17,2H7Z" 
         Width="16" Height="16"/>
```

**Refresh**
```xml
<PathIcon Data="M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z" 
         Width="16" Height="16"/>
```

**Play (Execute)**
```xml
<PathIcon Data="M8,5.14V19.14L19,12.14L8,5.14Z" 
         Width="16" Height="16"/>
```

**Stop**
```xml
<PathIcon Data="M18,18H6V6H18V18Z" 
         Width="16" Height="16"/>
```

### Files & Documents

**File Document**
```xml
<PathIcon Data="M13,9H18.5L13,3.5V9M6,2H14L20,8V20A2,2 0 0,1 18,22H6C4.89,22 4,21.1 4,20V4C4,2.89 4.89,2 6,2M15,18V16H6V18H15M18,14V12H6V14H18Z" 
         Width="16" Height="16"/>
```

**File Multiple**
```xml
<PathIcon Data="M15,7H20.5L15,1.5V7M8,0H16L22,6V18A2,2 0 0,1 20,20H8C6.89,20 6,19.1 6,18V2A2,2 0 0,1 8,0M4,4V22H20V24H4A2,2 0 0,1 2,22V4H4Z" 
         Width="16" Height="16"/>
```

### Edit & Modify

**Pencil (Edit)**
```xml
<PathIcon Data="M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z" 
         Width="16" Height="16"/>
```

**Delete**
```xml
<PathIcon Data="M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z" 
         Width="16" Height="16"/>
```

**Content Save**
```xml
<PathIcon Data="M15,9H5V5H15M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3Z" 
         Width="16" Height="16"/>
```

### Alerts & Status

**Alert Circle Outline**
```xml
<PathIcon Data="M11,15H13V17H11V15M11,7H13V13H11V7M12,2C6.47,2 2,6.5 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20Z" 
         Width="16" Height="16"/>
```

**Check Circle**
```xml
<PathIcon Data="M12 2C6.5 2 2 6.5 2 12S6.5 22 12 22 22 17.5 22 12 17.5 2 12 2M10 17L5 12L6.41 10.59L10 14.17L17.59 6.58L19 8L10 17Z" 
         Width="16" Height="16"/>
```

**Lock**
```xml
<PathIcon Data="M12,17A2,2 0 0,0 14,15C14,13.89 13.1,13 12,13A2,2 0 0,0 10,15A2,2 0 0,0 12,17M18,8A2,2 0 0,1 20,10V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V10C4,8.89 4.9,8 6,8H7V6A5,5 0 0,1 12,1A5,5 0 0,1 17,6V8H18M12,3A3,3 0 0,0 9,6V8H15V6A3,3 0 0,0 12,3Z" 
         Width="16" Height="16"/>
```

### Numeric & Counters

**Counter (123)**
```xml
<PathIcon Data="M4,4H7V14H9V16H4V14H6V6H4V4M13,4H16V6H11V8H14A2,2 0 0,1 16,10V11A2,2 0 0,1 14,13H10V11H14V10H11A2,2 0 0,1 9,8V7A2,2 0 0,1 11,5H13V4M4,18H7V20H4V18M13,18H16V20H13V18M8.5,18H11.5V20H8.5V18Z" 
         Width="16" Height="16"/>
```

**Numeric**
```xml
<PathIcon Data="M4,17V9H2V7H6V17H4M22,15C22,16.11 21.1,17 20,17H16V15H20V13H18V11H20V9H16V7H20A2,2 0 0,1 22,9V10.5A1.5,1.5 0 0,1 20.5,12A1.5,1.5 0 0,1 22,13.5V15M14,15V17H8V13C8,11.89 8.9,11 10,11H12V9H8V7H12A2,2 0 0,1 14,9V11C14,12.11 13.1,13 12,13H10V15H14Z" 
         Width="16" Height="16"/>
```

---

## Size Recommendations

| Use Case | Width | Height | Notes |
|----------|-------|--------|-------|
| TreeView | 16 | 16 | Default |
| Toolbar | 20 | 20 | Slightly larger |
| Large Icons | 24 | 24 | Emphasized |
| MenuItem | 16 | 16 | Default |

---

## Color Usage

### Default (Theme Color)
```xml
<PathIcon Data="..." Width="16" Height="16"/>
```

### Custom Color
```xml
<PathIcon Data="..." Width="16" Height="16" Foreground="Red"/>
<PathIcon Data="..." Width="16" Height="16" Foreground="#FF5722"/>
```

### Dynamic Color (Binding)
```xml
<PathIcon Data="..." 
         Width="16" 
         Height="16"
         Foreground="{Binding StatusColor}"/>
```

---

## Resources

- **Material Design Icons**: https://materialdesignicons.com/
- **Avalonia Docs**: https://docs.avaloniaui.net/docs/controls/pathicon
- **SVG Path Syntax**: https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/Paths

---

## Notes

1. All path data is from Material Design Icons (Apache 2.0 / OFL license)
2. Icons are designed for 24x24 viewport, scale well to 16x16
3. PathIcon is native Avalonia control - no external dependencies
4. All icons automatically adapt to theme colors

---

*Reference Guide for WitDatabase Studio Development*
