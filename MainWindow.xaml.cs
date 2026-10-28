using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Universum.Core;
using Universum.ViewModels;

namespace Universum;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.RefreshAsync();
        _ = SelfUpdate.CleanupAsync();
        Activated += (_, _) => _vm.RefreshInstalledOnly();
        StateChanged += (_, _) => UpdateMaximized();
    }

    void UpdateMaximized()
    {
        // Окно без рамки при разворачивании выходит за край экрана на толщину рамки.
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaxButton.Content = maximized ? "" : "";
    }

    void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = 36 - 210;
            menu.VerticalOffset = 6;
            menu.IsOpen = true;
        }
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Profile_Click(object sender, RoutedEventArgs e) => InstallService.OpenUrl("https://github.com/kolyaka6969-ops");
}
