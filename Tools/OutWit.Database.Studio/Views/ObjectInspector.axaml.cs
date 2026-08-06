using Avalonia.Controls;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// The right-hand panel: what the selected object is (WS-18). All of it is binding; there is nothing
/// for the code-behind to do beyond loading the markup.
/// </summary>
public partial class ObjectInspector : UserControl
{
    public ObjectInspector()
    {
        InitializeComponent();
    }
}
