using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using OneUI.App.Native;
using OneUI.Core.Models;

namespace OneUI.App.Views;

/// <summary>
/// Displays live hardware-accelerated DWM Thumbnail previews of running application windows
/// when hovering over a Dock icon.
/// </summary>
public partial class WindowPreviewPopup : Window
{
    private readonly List<nint> _registeredThumbnails = [];
    private readonly List<(nint SrcHwnd, Border Placeholder)> _pendingSlots = [];
    private MonitorBounds _currentMonitor = MonitorHelper.GetPrimaryMonitor();

    public event Action? PointerLeftPopup;

    public WindowPreviewPopup()
    {
        InitializeComponent();
        // DWM thumbnails require a standard non-layered HWND (which also gives native Win11 Acrylic blur!)
        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: true, noActivate: true);
    }

    public void ShowForItem(DockItem item, MonitorBounds monitor, double iconCenterXPhysical, int dockTopPhysical)
    {
        if (item.Windows.Count == 0)
        {
            HideAndClear();
            return;
        }

        ClearThumbnails();
        CardsPanel.Children.Clear();
        _pendingSlots.Clear();
        _currentMonitor = monitor;

        var windowsToShow = item.Windows.Take(4).ToList();

        foreach (var win in windowsToShow)
        {
            var card = new Border
            {
                Width = 204,
                Height = 148,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 5, 7, 7),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header: Window Title + Close button
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = win.Title,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextPrimaryBrush");

            var targetHwnd = win.Handle;
            var closeBtn = new Button
            {
                Style = (Style)FindResource("GlassPillButton"),
                Content = "×",
                FontSize = 12,
                Padding = new Thickness(5, 0, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Close Window"
            };
            closeBtn.Click += (_, args) =>
            {
                args.Handled = true;
                WindowTracker.CloseWindow(targetHwnd);
                HideAndClear();
            };

            Grid.SetColumn(titleText, 0);
            Grid.SetColumn(closeBtn, 1);
            headerGrid.Children.Add(titleText);
            headerGrid.Children.Add(closeBtn);

            // Live DWM Thumbnail Placeholder
            var thumbHost = new Border
            {
                Margin = new Thickness(0, 4, 0, 0),
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(35, 0, 0, 0))
            };

            Grid.SetRow(headerGrid, 0);
            Grid.SetRow(thumbHost, 1);
            cardGrid.Children.Add(headerGrid);
            cardGrid.Children.Add(thumbHost);

            card.Child = cardGrid;
            card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255));
            card.MouseLeave += (_, _) => card.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            card.MouseLeftButtonUp += (_, _) =>
            {
                HideAndClear();
                WindowTracker.ActivateWindow(targetHwnd);
            };

            CardsPanel.Children.Add(card);
            _pendingSlots.Add((targetHwnd, thumbHost));
        }

        if (!IsVisible) Show();
        UpdateLayout();

        // Position centered above the hovered Dock icon, clamped cleanly inside the current monitor
        var destHwnd = new WindowInteropHelper(this).Handle;
        if (destHwnd != nint.Zero)
        {
            int widthPx = (int)Math.Round(ActualWidth * monitor.DpiScaleX);
            int heightPx = (int)Math.Round(ActualHeight * monitor.DpiScaleY);
            int gapPx = (int)Math.Round(10 * monitor.DpiScaleY);
            int padPx = (int)Math.Round(12 * monitor.DpiScaleX);

            int xPx = (int)Math.Round(iconCenterXPhysical - widthPx / 2.0);
            xPx = Math.Clamp(xPx, monitor.PhysicalLeft + padPx, monitor.PhysicalLeft + monitor.PhysicalWidth - widthPx - padPx);
            int yPx = dockTopPhysical - heightPx - gapPx;

            Win32Api.SetWindowPos(
                destHwnd,
                Win32Api.HWND_TOPMOST,
                xPx,
                yPx,
                widthPx,
                heightPx,
                Win32Api.SWP_NOACTIVATE);

            // Register live DWM Thumbnails once layout coordinates are final
            Dispatcher.BeginInvoke(() => RegisterLiveThumbnails(destHwnd, monitor));
        }
    }

    private void RegisterLiveThumbnails(nint destHwnd, MonitorBounds monitor)
    {
        if (!IsVisible || destHwnd == nint.Zero) return;

        foreach (var (srcHwnd, placeholder) in _pendingSlots)
        {
            if (srcHwnd == nint.Zero || !Win32Api.IsWindow(srcHwnd)) continue;

            if (Win32Api.DwmRegisterThumbnail(destHwnd, srcHwnd, out nint thumbId) != 0 || thumbId == nint.Zero)
                continue;

            _registeredThumbnails.Add(thumbId);

            // Convert placeholder bounds relative to this window into physical pixels
            var topLeft = placeholder.TranslatePoint(new Point(0, 0), this);
            double slotW = placeholder.ActualWidth;
            double slotH = placeholder.ActualHeight;
            if (slotW <= 2 || slotH <= 2) continue;

            int destLeft = (int)Math.Round(topLeft.X * monitor.DpiScaleX);
            int destTop = (int)Math.Round(topLeft.Y * monitor.DpiScaleY);
            int destW = (int)Math.Round(slotW * monitor.DpiScaleX);
            int destH = (int)Math.Round(slotH * monitor.DpiScaleY);

            // Preserve the source window's aspect ratio cleanly inside the slot
            if (Win32Api.DwmQueryThumbnailSourceSize(thumbId, out var srcSize) == 0 && srcSize.cx > 0 && srcSize.cy > 0)
            {
                double scale = Math.Min((double)destW / srcSize.cx, (double)destH / srcSize.cy);
                int fittedW = Math.Max(1, (int)Math.Round(srcSize.cx * scale));
                int fittedH = Math.Max(1, (int)Math.Round(srcSize.cy * scale));
                destLeft += (destW - fittedW) / 2;
                destTop += (destH - fittedH) / 2;
                destW = fittedW;
                destH = fittedH;
            }

            var props = new Win32Api.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Win32Api.DWM_TNP_RECTDESTINATION | Win32Api.DWM_TNP_VISIBLE | Win32Api.DWM_TNP_OPACITY | Win32Api.DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new Win32Api.RECT
                {
                    Left = destLeft,
                    Top = destTop,
                    Right = destLeft + destW,
                    Bottom = destTop + destH
                },
                opacity = 255,
                fVisible = true,
                fSourceClientAreaOnly = false
            };

            _ = Win32Api.DwmUpdateThumbnailProperties(thumbId, ref props);
        }
    }

    public void HideAndClear()
    {
        ClearThumbnails();
        if (IsVisible) Hide();
    }

    private void ClearThumbnails()
    {
        foreach (var thumb in _registeredThumbnails)
        {
            _ = Win32Api.DwmUnregisterThumbnail(thumb);
        }
        _registeredThumbnails.Clear();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        PointerLeftPopup?.Invoke();
    }
}
