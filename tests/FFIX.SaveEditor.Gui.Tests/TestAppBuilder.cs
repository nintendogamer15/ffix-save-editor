using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using SaveEditor.Ui.Theming;

[assembly: AvaloniaTestApplication(typeof(FFIX.SaveEditor.Gui.Tests.TestAppBuilder))]

namespace FFIX.SaveEditor.Gui.Tests;

public sealed class TestAppBuilder : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new SaveEditorTheme());
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestAppBuilder>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();
}
