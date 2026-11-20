using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;

namespace DesktopAgent.App.Views;

internal sealed class TextPromptWindow : Window
{
    private readonly System.Windows.Controls.TextBox _input;
    public string Value => _input.Text.Trim();

    public TextPromptWindow(string title, string label, string initial)
    {
        Title = title; Width = 470; Height = 180; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new Grid { Margin = new Thickness(20) };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new()); panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        _input = new System.Windows.Controls.TextBox { Text = initial, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(_input, 1); panel.Children.Add(_input);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new WpfButton { Content = "Cancel", Background = System.Windows.Media.Brushes.LightGray, Foreground = System.Windows.Media.Brushes.Black };
        cancel.Click += (_, _) => Close();
        var ok = new WpfButton { Content = "Add", IsDefault = true }; ok.Click += (_, _) => { if (Value.Length > 0) DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(ok); Grid.SetRow(buttons, 2); panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => { _input.Focus(); _input.CaretIndex = _input.Text.Length; };
    }
}

internal sealed class ChoiceWindow : Window
{
    private readonly System.Windows.Controls.ListBox _list;
    public string? SelectedValue => _list.SelectedItem?.ToString();

    public ChoiceWindow(string title, string message, IReadOnlyList<string> choices)
    {
        Title = title; Width = 430; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new Grid { Margin = new Thickness(20) };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new()); panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        _list = new System.Windows.Controls.ListBox { ItemsSource = choices }; _list.MouseDoubleClick += (_, _) => Accept(); Grid.SetRow(_list, 1); panel.Children.Add(_list);
        var choose = new WpfButton { Content = "Choose", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, IsDefault = true }; choose.Click += (_, _) => Accept(); Grid.SetRow(choose, 2); panel.Children.Add(choose);
        Content = panel;
    }
    private void Accept() { if (_list.SelectedItem is not null) DialogResult = true; }
}
