// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace FFIX.SaveEditor.Gui;

internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string message)
    {
        Title = "Confirm in-place write";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var confirm = new Button { Content = "Overwrite", MinWidth = 100 };
        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 18,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10,
                    Children = { cancel, confirm },
                },
            },
        };
    }

    public static Task<bool> Ask(Window owner, string message) => new ConfirmDialog(message).ShowDialog<bool>(owner);
}
