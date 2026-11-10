using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace NetGuardian.App.Presentation;

internal static class DialogPresentation
{
    // 沿用原系统确认框的本地化按钮和访问键，不新增或翻译业务文案。
    internal static string YesButtonText => NativeButtonText(5);
    internal static string NoButtonText => NativeButtonText(6);

    private static string NativeButtonText(uint id) =>
        (Marshal.PtrToStringUni(MB_GetString(id)) ?? throw new InvalidOperationException("无法读取系统确认按钮文字。"))
        .Replace("_", "__").Replace("&", "_");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr MB_GetString(uint id);

    internal static FluentConfirmationDialog Create(Window owner, string title, string message,
        string primary, string cancel, string? secondary = null, bool dangerous = false, bool defaultCancel = false) =>
        new(owner, title, message, primary, cancel, secondary, dangerous, defaultCancel);

    internal static Task<MessageBoxResult> ShowAsync(Window owner, string title, string message,
        string primary, string cancel, string? secondary = null) =>
        Task.FromResult(Show(owner, title, message, primary, cancel, secondary));

    internal static MessageBoxResult Show(Window owner, string title, string message,
        string primary, string cancel, string? secondary = null, bool dangerous = false, bool defaultCancel = false)
    {
        var dialog = Create(owner, title, message, primary, cancel, secondary, dangerous, defaultCancel);
        dialog.ShowDialog();
        return dialog.Result;
    }
}

internal sealed class FluentConfirmationDialog : FluentDialogWindow
{
    internal MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    internal FluentConfirmationDialog(Window owner, string title, string message, string primary,
        string cancel, string? secondary, bool dangerous, bool defaultCancel)
    {
        Owner = owner;
        Title = title;
        Language = owner.Language;
        var body = new StackPanel();
        if (dangerous)
        {
            var warning = new SymbolIcon(SymbolRegular.Warning24) { HorizontalAlignment = HorizontalAlignment.Left };
            warning.SetResourceReference(ForegroundProperty, "StatusWarningBrush");
            warning.SetResourceReference(MarginProperty, "SpacingFieldGap");
            body.Children.Add(warning);
        }
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(FontSizeProperty, "FontSubtitle");
        heading.SetResourceReference(MarginProperty, "SpacingFieldGap");
        body.Children.Add(heading);
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        Body.Content = body;
        AddButton(primary, dangerous ? ControlAppearance.Danger : ControlAppearance.Primary, MessageBoxResult.Primary, !defaultCancel);
        if (secondary is not null) AddButton(secondary, ControlAppearance.Secondary, MessageBoxResult.Secondary, false);
        var cancelButton = AddButton(cancel, ControlAppearance.Secondary, MessageBoxResult.None, defaultCancel);
        cancelButton.IsCancel = true;
        Loaded += (_, _) =>
        {
            if (defaultCancel) cancelButton.Focus();
        };
    }

    private Wpf.Ui.Controls.Button AddButton(string text, ControlAppearance appearance, MessageBoxResult result, bool isDefault)
    {
        var button = new Wpf.Ui.Controls.Button { Content = text, Appearance = appearance, IsDefault = isDefault };
        button.SetResourceReference(StyleProperty, "DialogButtonStyle");
        button.Click += (_, _) => { Result = result; DialogResult = result != MessageBoxResult.None; };
        Actions.Children.Add(button);
        return button;
    }
}
