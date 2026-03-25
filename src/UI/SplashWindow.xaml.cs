namespace schud.UI;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetHotkeyLabel(string hotkey) => HotkeyLabel.Text = hotkey;

    private void OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Close();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var color = 0x00281A0C; // #0c1a28 as COLORREF — matches SettingsWindow caption colour
        DwmSetWindowAttribute(hwnd, 35, ref color, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);
}
