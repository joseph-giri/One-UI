using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OneUI.App.Native;

namespace OneUI.App.Views;

public partial class ScreenColorPickerWindow : Window
{
    private string _currentHex = "#FFFFFF";
    public event Action<string>? ColorPicked;

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int x, int y);

    public ScreenColorPickerWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Loaded += (_, _) => UpdateSampleAtCursor();
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateSampleAtCursor(e.GetPosition(OverlayCanvas));
    }

    private void UpdateSampleAtCursor(Point? canvasPos = null)
    {
        if (Win32Api.GetCursorPos(out var pt))
        {
            nint hdc = GetDC(nint.Zero);
            if (hdc != nint.Zero)
            {
                try
                {
                    uint pixel = GetPixel(hdc, pt.X, pt.Y);
                    byte r = (byte)(pixel & 0x000000FF);
                    byte g = (byte)((pixel & 0x0000FF00) >> 8);
                    byte b = (byte)((pixel & 0x00FF0000) >> 16);
                    _currentHex = $"#{r:X2}{g:X2}{b:X2}";
                    ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
                    HexText.Text = _currentHex;
                }
                finally
                {
                    ReleaseDC(nint.Zero, hdc);
                }
            }
        }

        if (canvasPos.HasValue)
        {
            double x = canvasPos.Value.X + 18;
            double y = canvasPos.Value.Y + 18;
            if (x + 190 > ActualWidth) x = canvasPos.Value.X - 196;
            if (y + 56 > ActualHeight) y = canvasPos.Value.Y - 62;
            Canvas.SetLeft(PreviewPill, x);
            Canvas.SetTop(PreviewPill, y);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        UpdateSampleAtCursor();
        ColorPicked?.Invoke(_currentHex);
        Close();
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => Close();

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
