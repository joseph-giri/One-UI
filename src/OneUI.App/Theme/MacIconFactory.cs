using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using OneUI.Core.Models;

namespace OneUI.App.Theme;

/// <summary>
/// Builds rich, multi-layered Apple Human Interface–style squircle icons with
/// depth gradients, inner specular bevels, and vector artwork.
/// </summary>
public static class MacIconFactory
{
    public static FrameworkElement CreateDockSquircle(DockItem item, double size, Transform renderTransform)
    {
        var kind = ClassifyItem(item);
        var (topColor, bottomColor) = GetPalette(kind);

        var rootGrid = new Grid
        {
            Width = size,
            Height = size
        };

        // 1. Base Squircle with rich vertical gradient + specular rim
        var baseSquircle = new Border
        {
            CornerRadius = new CornerRadius(size * 0.235),
            Background = new LinearGradientBrush(topColor, bottomColor, 90),
            BorderBrush = new LinearGradientBrush(
                Color.FromArgb(145, 255, 255, 255),
                Color.FromArgb(35, 255, 255, 255),
                new Point(0, 0),
                new Point(1, 1)),
            BorderThickness = new Thickness(1)
        };

        // 2. Top Inner Convex Glass Highlight
        var topSheen = new Border
        {
            CornerRadius = new CornerRadius(size * 0.22),
            Margin = new Thickness(1),
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(
                Color.FromArgb(64, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                new Point(0.5, 0),
                new Point(0.5, 0.55))
        };

        // 3. Bespoke Multi-Layer Vector Artwork
        var artwork = BuildArtwork(kind, size);

        rootGrid.Children.Add(baseSquircle);
        rootGrid.Children.Add(topSheen);
        rootGrid.Children.Add(artwork);

        return new Border
        {
            Width = size,
            Height = size,
            Cursor = System.Windows.Input.Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 1.0),
            RenderTransform = renderTransform,
            Effect = new DropShadowEffect
            {
                BlurRadius = 11,
                ShadowDepth = 3,
                Opacity = 0.42,
                Color = Colors.Black
            },
            Child = rootGrid
        };
    }

    private enum IconKind
    {
        Start,
        TaskView,
        FileExplorer,
        Browser,
        Terminal,
        Settings,
        Notepad,
        Calculator,
        Trash,
        Generic
    }

    private static IconKind ClassifyItem(DockItem item)
    {
        if (item.Id == "special:start") return IconKind.Start;
        if (item.Id == "special:taskview") return IconKind.TaskView;
        if (item.Id == "special:trash") return IconKind.Trash;

        var key = (item.Name + " " + item.ExecutablePath).ToLowerInvariant();
        if (key.Contains("explorer")) return IconKind.FileExplorer;
        if (key.Contains("edge") || key.Contains("chrome") || key.Contains("firefox")) return IconKind.Browser;
        if (key.Contains("terminal") || key.Contains("wt") || key.Contains("powershell") || key.Contains("cmd")) return IconKind.Terminal;
        if (key.Contains("settings")) return IconKind.Settings;
        if (key.Contains("note")) return IconKind.Notepad;
        if (key.Contains("calc")) return IconKind.Calculator;
        return IconKind.Generic;
    }

    private static (Color Top, Color Bottom) GetPalette(IconKind kind) => kind switch
    {
        IconKind.Start => (Color.FromRgb(42, 168, 255), Color.FromRgb(0, 98, 224)),
        IconKind.TaskView => (Color.FromRgb(126, 94, 255), Color.FromRgb(74, 48, 208)),
        IconKind.FileExplorer => (Color.FromRgb(72, 198, 255), Color.FromRgb(12, 118, 242)),
        IconKind.Browser => (Color.FromRgb(36, 208, 232), Color.FromRgb(14, 112, 230)),
        IconKind.Terminal => (Color.FromRgb(54, 58, 70), Color.FromRgb(20, 22, 30)),
        IconKind.Settings => (Color.FromRgb(156, 164, 178), Color.FromRgb(86, 92, 106)),
        IconKind.Notepad => (Color.FromRgb(255, 222, 82), Color.FromRgb(242, 168, 18)),
        IconKind.Calculator => (Color.FromRgb(64, 68, 80), Color.FromRgb(32, 34, 42)),
        IconKind.Trash => (Color.FromRgb(142, 150, 166), Color.FromRgb(84, 90, 104)),
        _ => (Color.FromRgb(98, 114, 248), Color.FromRgb(62, 72, 208))
    };

    private static UIElement BuildArtwork(IconKind kind, double size)
    {
        switch (kind)
        {
            case IconKind.Start:
            {
                // Luminous 4-pane glass Windows emblem
                var grid = new Grid
                {
                    Width = size * 0.48,
                    Height = size * 0.48,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                grid.RowDefinitions.Add(new RowDefinition());
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2.2) });
                grid.RowDefinitions.Add(new RowDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());

                for (int r = 0; r <= 2; r += 2)
                {
                    for (int c = 0; c <= 2; c += 2)
                    {
                        var pane = new Border
                        {
                            CornerRadius = new CornerRadius(2),
                            Background = new LinearGradientBrush(
                                Colors.White,
                                Color.FromArgb(215, 225, 242, 255),
                                45)
                        };
                        Grid.SetRow(pane, r);
                        Grid.SetColumn(pane, c);
                        grid.Children.Add(pane);
                    }
                }
                return grid;
            }

            case IconKind.TaskView:
            {
                // Layered glass workspace cards (Mission Control / Task View aesthetic)
                var canvas = new Grid
                {
                    Width = size * 0.56,
                    Height = size * 0.50,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                canvas.Children.Add(new Border
                {
                    Width = size * 0.34,
                    Height = size * 0.24,
                    CornerRadius = new CornerRadius(3.5),
                    Background = new SolidColorBrush(Color.FromArgb(145, 255, 255, 255)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                });
                canvas.Children.Add(new Border
                {
                    Width = size * 0.34,
                    Height = size * 0.24,
                    CornerRadius = new CornerRadius(3.5),
                    Background = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                canvas.Children.Add(new Border
                {
                    Width = size * 0.44,
                    Height = size * 0.26,
                    CornerRadius = new CornerRadius(4),
                    Background = Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                });
                return canvas;
            }

            case IconKind.FileExplorer:
            {
                // Two-tone sculpted glass folder
                var folder = new Grid
                {
                    Width = size * 0.58,
                    Height = size * 0.46,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                folder.Children.Add(new Border
                {
                    Width = size * 0.24,
                    Height = size * 0.12,
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(195, 255, 255, 255)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                });
                folder.Children.Add(new Border
                {
                    Margin = new Thickness(0, size * 0.07, 0, 0),
                    CornerRadius = new CornerRadius(4.5),
                    Background = new LinearGradientBrush(Colors.White, Color.FromRgb(220, 238, 255), 90),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    BorderThickness = new Thickness(0.8)
                });
                return folder;
            }

            case IconKind.Terminal:
            {
                // Sleek macOS Terminal ">_" prompt in luminous green/white
                var stack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(size * 0.18, size * 0.20, 0, 0)
                };
                stack.Children.Add(new TextBlock
                {
                    Text = ">_",
                    FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
                    FontWeight = FontWeights.Bold,
                    FontSize = size * 0.30,
                    Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88))
                });
                return stack;
            }

            case IconKind.Calculator:
            {
                // 2x2 Apple Calculator pill grid with orange operator tile
                var grid = new UniformGrid
                {
                    Rows = 2,
                    Columns = 2,
                    Width = size * 0.54,
                    Height = size * 0.54,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                UIElement MakeBtn(string sym, Color bg) => new Border
                {
                    Margin = new Thickness(1.6),
                    CornerRadius = new CornerRadius(size * 0.11),
                    Background = new SolidColorBrush(bg),
                    Child = new TextBlock
                    {
                        Text = sym,
                        FontSize = size * 0.18,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                grid.Children.Add(MakeBtn("+", Color.FromArgb(95, 255, 255, 255)));
                grid.Children.Add(MakeBtn("−", Color.FromArgb(95, 255, 255, 255)));
                grid.Children.Add(MakeBtn("×", Color.FromArgb(95, 255, 255, 255)));
                grid.Children.Add(MakeBtn("=", Color.FromRgb(255, 159, 10)));
                return grid;
            }

            case IconKind.Notepad:
            {
                // Clean Apple Notes paper card with header band & ruled lines
                var page = new Border
                {
                    Width = size * 0.52,
                    Height = size * 0.58,
                    CornerRadius = new CornerRadius(4.5),
                    Background = Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ClipToBounds = true
                };
                var lines = new StackPanel();
                lines.Children.Add(new Border { Height = size * 0.13, Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)) });
                for (int i = 0; i < 3; i++)
                {
                    lines.Children.Add(new Border
                    {
                        Height = 2,
                        Margin = new Thickness(4, size * 0.08, 4, 0),
                        CornerRadius = new CornerRadius(1),
                        Background = new SolidColorBrush(Color.FromRgb(205, 212, 224))
                    });
                }
                page.Child = lines;
                return page;
            }

            default:
            {
                // Crisp Segoe Fluent Icons fallback (with graceful fallback to Segoe MDL2 Assets)
                string glyph = kind switch
                {
                    IconKind.Settings => "\uE713",
                    IconKind.Trash => "\uE74D",
                    IconKind.Browser => "\uE774",
                    _ => "\uE74C"
                };
                return new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = size * 0.46,
                    Foreground = Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }
    }
}
