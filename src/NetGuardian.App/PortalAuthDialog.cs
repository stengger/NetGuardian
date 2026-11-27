using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using NetGuardian.Core.Portal;

namespace NetGuardian.App;

public sealed record PortalDetectionFeedback(
    bool Found,
    string Message,
    Uri? DiscoveredEntryUri = null,
    PortalInspectionResult? Inspection = null);

public sealed record PortalTestFeedback(
    bool Success,
    string Message,
    PortalAuthConfig? VerifiedConfig = null);

public sealed class PortalAuthDialog : FluentDialogWindow
{
    private readonly Localizer _localizer;
    private readonly string _ssid;
    private readonly PortalAuthConfig? _initialConfig;
    private readonly bool _hasExistingCredential;
    private readonly Func<CancellationToken, Task<PortalDetectionFeedback>>? _detectHandler;
    private readonly Func<PortalAuthConfig, PortalCredential?, Uri?, CancellationToken, Task<PortalTestFeedback>>? _testHandler;

    private readonly TextBlock _statusBanner = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _modeBox = new() { Name = "PortalModeInput" };
    private readonly ComboBox _strategyBox = new() { Name = "PortalStrategyInput" };
    private readonly TextBox _originBox = new() { Name = "PortalOriginInput" };
    private readonly TextBox _loginPathBox = new() { Name = "PortalLoginPathInput" };
    private readonly TextBox _submitPathBox = new() { Name = "PortalSubmitPathInput" };
    private readonly CheckBox _allowHttpCheck = new() { Name = "PortalAllowHttpCheck" };
    private readonly ComboBox _userSelectorBox = new() { Name = "PortalUserSelectorInput", IsEditable = true, IsTextSearchEnabled = false };
    private readonly ComboBox _passSelectorBox = new() { Name = "PortalPassSelectorInput", IsEditable = true, IsTextSearchEnabled = false };
    private readonly ComboBox _submitSelectorBox = new() { Name = "PortalSubmitSelectorInput", IsEditable = true, IsTextSearchEnabled = false };
    private readonly ComboBox _selectSelectorBox = new() { Name = "PortalSelectSelectorInput", IsEditable = true, IsTextSearchEnabled = false };
    private readonly ComboBox _selectValueBox = new() { Name = "PortalSelectValueInput", IsEditable = true, IsTextSearchEnabled = false };
    private readonly TextBox _accountBox = new() { Name = "PortalAccountInput" };
    private readonly Wpf.Ui.Controls.PasswordBox _passwordBox = new() { Name = "PortalPasswordInput", RevealButtonEnabled = true };
    private readonly TextBlock _feedbackText = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly StackPanel _credentialsGroup = new();

    private readonly Wpf.Ui.Controls.Button _detectButton;
    private readonly Wpf.Ui.Controls.Button _testButton;
    private readonly Wpf.Ui.Controls.Button _savePendingButton;
    private readonly Wpf.Ui.Controls.Button _clearButton;
    private readonly Wpf.Ui.Controls.Button _cancelButton;

    private readonly Dictionary<string, IReadOnlyList<PortalSelectOption>> _selectOptionsBySelector = new(StringComparer.Ordinal);
    private Uri? _discoveredEntryUri;
    private bool _isBusy;

    public PortalAuthDialog(
        Localizer localizer,
        string ssid,
        PortalAuthConfig? existingConfig = null,
        string? existingAccount = null,
        bool hasExistingCredential = false,
        Func<CancellationToken, Task<PortalDetectionFeedback>>? detectHandler = null,
        Func<PortalAuthConfig, PortalCredential?, Uri?, CancellationToken, Task<PortalTestFeedback>>? testHandler = null)
    {
        _localizer = localizer;
        _ssid = ssid;
        _initialConfig = existingConfig;
        _hasExistingCredential = hasExistingCredential;
        _detectHandler = detectHandler;
        _testHandler = testHandler;

        Title = $"{localizer.T("PortalAuthTitle")} - {ssid}";
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(localizer.Language);

        var root = new StackPanel();
        Body.Content = root;

        var title = new TextBlock { Text = $"{localizer.T("PortalAuthTitle")} · {ssid}", FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(FontSizeProperty, "FontSubtitle");
        title.SetResourceReference(MarginProperty, "SpacingSectionGap");
        root.Children.Add(title);

        _statusBanner.SetResourceReference(MarginProperty, "SpacingFieldGap");
        _statusBanner.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        _statusBanner.Text = FormatCurrentStatus(existingConfig);
        root.Children.Add(_statusBanner);

        _detectButton = new Wpf.Ui.Controls.Button
        {
            Name = "PortalDetectButton",
            Content = localizer.T("PortalDetectButton"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _detectButton.SetResourceReference(StyleProperty, "DialogButtonStyle");
        _detectButton.SetResourceReference(MarginProperty, "SpacingFieldGap");
        _detectButton.IsEnabled = detectHandler is not null;
        _detectButton.Click += async (_, _) => await DetectAsync();
        root.Children.Add(_detectButton);

        _modeBox.Items.Add(new ComboBoxItem { Content = localizer.T("PortalModeCredentials"), Tag = PortalAuthMode.CredentialsForm });
        _modeBox.Items.Add(new ComboBoxItem { Content = localizer.T("PortalModeConsent"), Tag = PortalAuthMode.OneClickConsent });
        _modeBox.SetResourceReference(StyleProperty, "DialogComboBoxStyle");
        _modeBox.SelectedIndex = existingConfig?.Mode == PortalAuthMode.OneClickConsent ? 1 : 0;
        _modeBox.SelectionChanged += (_, _) => UpdateModeVisibility();
        AddField(root, localizer.T("PortalModeLabel"), _modeBox);

        _strategyBox.Items.Add(new ComboBoxItem { Content = localizer.T("PortalFillPlaceholder"), Tag = PortalFillStrategy.PlaceholderSubstitution });
        _strategyBox.Items.Add(new ComboBoxItem { Content = localizer.T("PortalFillScopedBrowser"), Tag = PortalFillStrategy.ScopedBrowserFill });
        _strategyBox.SetResourceReference(StyleProperty, "DialogComboBoxStyle");
        _strategyBox.SelectedIndex = existingConfig?.FillStrategy == PortalFillStrategy.ScopedBrowserFill ? 1 : 0;
        AddField(root, localizer.T("PortalFillStrategyLabel"), _strategyBox);

        _originBox.SetResourceReference(StyleProperty, "DialogTextBoxStyle");
        _originBox.Text = existingConfig?.AllowedOrigin ?? "";
        AddField(root, localizer.T("PortalOriginLabel"), _originBox);

        _loginPathBox.SetResourceReference(StyleProperty, "DialogTextBoxStyle");
        _loginPathBox.Text = string.IsNullOrWhiteSpace(existingConfig?.AllowedLoginPath) ? "/" : existingConfig.AllowedLoginPath;
        AddField(root, localizer.T("PortalLoginPathLabel"), _loginPathBox);

        _submitPathBox.SetResourceReference(StyleProperty, "DialogTextBoxStyle");
        _submitPathBox.Text = existingConfig?.AllowedSubmitPath ?? "";
        AddField(root, localizer.T("PortalSubmitPathLabel"), _submitPathBox);

        _allowHttpCheck.Content = localizer.T("PortalAllowHttpCheck");
        _allowHttpCheck.IsChecked = existingConfig?.AllowInsecureHttp == true;
        _allowHttpCheck.SetResourceReference(MarginProperty, "SpacingOptionGap");
        root.Children.Add(_allowHttpCheck);

        _userSelectorBox.SetResourceReference(StyleProperty, "DialogComboBoxStyle");
        _userSelectorBox.Text = existingConfig?.UsernameSelector ?? "input[autocomplete=username]";
        _passSelectorBox.SetResourceReference(StyleProperty, "DialogComboBoxStyle");
        _passSelectorBox.Text = existingConfig?.PasswordSelector ?? "input[type=password]";
        _submitSelectorBox.SetResourceReference(StyleProperty, "DialogComboBoxStyle");
        _submitSelectorBox.Text = string.IsNullOrWhiteSpace(existingConfig?.SubmitSelector) ? "button[type=submit]" : existingConfig.SubmitSelector;

        _accountBox.SetResourceReference(StyleProperty, "DialogTextBoxStyle");
        _accountBox.Text = existingAccount ?? "";
        _passwordBox.SetResourceReference(StyleProperty, "DialogPasswordBoxStyle");
        FormField.SetRevealLabel(_passwordBox, localizer.T("Password"));
        if (hasExistingCredential)
            _passwordBox.PlaceholderText = localizer.T("PortalPasswordLeaveBlankHint");

        AddField(_credentialsGroup, localizer.T("PortalAccountLabel"), _accountBox);
        AddField(_credentialsGroup, localizer.T("Password"), _passwordBox);
        AddField(_credentialsGroup, localizer.T("PortalUsernameSelectorLabel"), _userSelectorBox);
        AddField(_credentialsGroup, localizer.T("PortalPasswordSelectorLabel"), _passSelectorBox);
        root.Children.Add(_credentialsGroup);

        AddField(root, localizer.T("PortalSubmitSelectorLabel"), _submitSelectorBox);

        _selectSelectorBox.SetResourceReference(StyleProperty, "DialogComboBoxStyle");
        _selectValueBox.SetResourceReference(StyleProperty, "DialogComboBoxStyle");
        var firstSelect = existingConfig?.FixedSelectValues.FirstOrDefault();
        _selectSelectorBox.Text = firstSelect?.Selector ?? "";
        _selectValueBox.Text = firstSelect?.Value ?? "";
        _selectSelectorBox.SelectionChanged += (_, _) => PopulateSelectValues();
        AddField(root, localizer.T("PortalSelectSelectorLabel"), _selectSelectorBox);
        AddField(root, localizer.T("PortalSelectValueLabel"), _selectValueBox);

        _feedbackText.SetResourceReference(MarginProperty, "SpacingValidationGap");
        AutomationProperties.SetLiveSetting(_feedbackText, AutomationLiveSetting.Polite);
        root.Children.Add(_feedbackText);

        _testButton = new Wpf.Ui.Controls.Button
        {
            Name = "PortalTestButton",
            Content = localizer.T("PortalTestAndSaveButton"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            IsDefault = true
        };
        _savePendingButton = new Wpf.Ui.Controls.Button
        {
            Name = "PortalSavePendingButton",
            Content = localizer.T("PortalSavePendingButton"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary
        };
        _clearButton = new Wpf.Ui.Controls.Button
        {
            Name = "PortalClearButton",
            Content = localizer.T("PortalClearButton"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Visibility = existingConfig is not null ? Visibility.Visible : Visibility.Collapsed
        };
        _cancelButton = new Wpf.Ui.Controls.Button
        {
            Name = "PortalCancelButton",
            Content = localizer.T("Cancel"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            IsCancel = true
        };

        foreach (var button in new[] { _testButton, _savePendingButton, _clearButton, _cancelButton })
        {
            button.SetResourceReference(StyleProperty, "DialogButtonStyle");
            button.SetResourceReference(MarginProperty, "SpacingLeadingGap");
            Actions.Children.Add(button);
        }

        _testButton.IsEnabled = testHandler is not null;
        _testButton.Click += async (_, _) => await TestAndSaveAsync();
        _savePendingButton.Click += (_, _) => SavePending();
        _clearButton.Click += (_, _) => ClearSetup();
        _cancelButton.Click += (_, _) => DialogResult = false;

        UpdateModeVisibility();
    }

    public PortalAuthConfig? ResultConfig { get; private set; }
    public PortalCredential? ResultCredential { get; private set; }
    public bool ClearRequested { get; private set; }

    private PortalAuthMode SelectedMode =>
        _modeBox.SelectedItem is ComboBoxItem { Tag: PortalAuthMode mode } ? mode : PortalAuthMode.CredentialsForm;

    private PortalFillStrategy SelectedStrategy =>
        _strategyBox.SelectedItem is ComboBoxItem { Tag: PortalFillStrategy strategy } ? strategy : PortalFillStrategy.PlaceholderSubstitution;

    private void UpdateModeVisibility()
    {
        var isCredentials = SelectedMode == PortalAuthMode.CredentialsForm;
        _credentialsGroup.Visibility = isCredentials ? Visibility.Visible : Visibility.Collapsed;
        _strategyBox.IsEnabled = !_isBusy && isCredentials;
    }

    private string FormatCurrentStatus(PortalAuthConfig? config)
    {
        if (config is null) return _localizer.T("PortalStatusUnconfigured");
        if (config.Paused) return string.Format(_localizer.T("PortalStatusPaused"), config.PauseReason ?? "—");
        if (config.CanAutoAuthenticate) return string.Format(_localizer.T("PortalStatusReady"), config.AllowedOrigin);
        return string.Format(_localizer.T("PortalStatusPending"), string.IsNullOrWhiteSpace(config.AllowedOrigin) ? "—" : config.AllowedOrigin);
    }

    private async Task DetectAsync()
    {
        if (_detectHandler is null || _isBusy) return;
        SetBusy(true, _localizer.T("PortalDetecting"), isError: false);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var feedback = await _detectHandler(cts.Token);
            if (feedback.DiscoveredEntryUri is not null) _discoveredEntryUri = feedback.DiscoveredEntryUri;

            if (!feedback.Found || feedback.Inspection is null)
            {
                ShowBanner(feedback.Message, isError: false);
                return;
            }

            var info = feedback.Inspection;
            if (!string.IsNullOrWhiteSpace(info.Origin)) _originBox.Text = info.Origin;
            if (!string.IsNullOrWhiteSpace(info.LoginPath)) _loginPathBox.Text = info.LoginPath;
            if (!string.IsNullOrWhiteSpace(info.SubmitPath)) _submitPathBox.Text = info.SubmitPath;
            _allowHttpCheck.IsChecked = !info.IsHttps;

            _userSelectorBox.ItemsSource = info.UsernameCandidates.Select(c => c.Selector).ToArray();
            _passSelectorBox.ItemsSource = info.PasswordCandidates.Select(c => c.Selector).ToArray();
            _submitSelectorBox.ItemsSource = info.SubmitCandidates.Select(c => c.Selector).ToArray();

            if (info.UsernameCandidates.Count > 0) _userSelectorBox.Text = info.UsernameCandidates[0].Selector;
            if (info.PasswordCandidates.Count > 0) _passSelectorBox.Text = info.PasswordCandidates[0].Selector;
            if (info.SubmitCandidates.Count > 0) _submitSelectorBox.Text = info.SubmitCandidates[0].Selector;

            if (info.UsernameCandidates.Count == 0 && info.PasswordCandidates.Count == 0 && info.SubmitCandidates.Count > 0)
                _modeBox.SelectedIndex = 1;
            else
                _modeBox.SelectedIndex = 0;

            _selectOptionsBySelector.Clear();
            foreach (var sel in info.SelectCandidates)
            {
                if (sel.Options is not null) _selectOptionsBySelector[sel.Selector] = sel.Options;
            }
            _selectSelectorBox.ItemsSource = info.SelectCandidates.Select(c => c.Selector).ToArray();
            if (info.SelectCandidates.Count > 0)
            {
                _selectSelectorBox.Text = info.SelectCandidates[0].Selector;
                PopulateSelectValues();
            }

            ShowBanner(feedback.Message, isError: false);
        }
        catch (Exception ex)
        {
            ShowBanner(ex.Message, isError: true);
        }
        finally
        {
            SetBusy(false, null, isError: false);
        }
    }

    private void PopulateSelectValues()
    {
        var selector = _selectSelectorBox.Text.Trim();
        if (_selectOptionsBySelector.TryGetValue(selector, out var options))
        {
            _selectValueBox.ItemsSource = options.Select(o => o.Value).ToArray();
            if (options.Count > 0 && string.IsNullOrWhiteSpace(_selectValueBox.Text))
                _selectValueBox.Text = options[0].Value;
        }
    }

    private bool TryBuildInput(bool ready, out PortalAuthConfig? config, out PortalCredential? credential)
    {
        config = null;
        credential = null;
        var allowHttp = _allowHttpCheck.IsChecked == true;
        var originText = _originBox.Text.Trim();

        if (!PortalAuthConfig.TryNormalizeOrigin(originText, allowHttp: true, out var parsedOrigin))
        {
            ShowBanner(_localizer.T("PortalValidationFieldsRequired"), isError: true);
            return false;
        }

        if (parsedOrigin.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !allowHttp)
        {
            ShowBanner(_localizer.T("PortalValidationHttpsRequired"), isError: true);
            return false;
        }

        var mode = SelectedMode;
        var credId = _initialConfig?.CredentialId;
        if (mode == PortalAuthMode.CredentialsForm)
        {
            var account = _accountBox.Text.Trim();
            var password = _passwordBox.Password;

            if (string.IsNullOrWhiteSpace(account))
            {
                ShowBanner(_localizer.T("PortalValidationFieldsRequired"), isError: true);
                return false;
            }

            if (string.IsNullOrEmpty(password))
            {
                if (!_hasExistingCredential || !PortalAuthConfig.IsValidCredentialId(credId))
                {
                    ShowBanner(_localizer.T("PortalValidationFieldsRequired"), isError: true);
                    return false;
                }
            }
            else
            {
                credId = PortalAuthConfig.IsValidCredentialId(credId) ? credId : Guid.NewGuid().ToString("N");
                credential = new PortalCredential(account, password);
            }
        }
        else
        {
            credId = null;
        }

        var selects = new List<PortalFieldSelection>();
        if (!string.IsNullOrWhiteSpace(_selectSelectorBox.Text))
            selects.Add(new PortalFieldSelection { Selector = _selectSelectorBox.Text.Trim(), Value = _selectValueBox.Text });

        var built = new PortalAuthConfig
        {
            Enabled = true,
            Ready = ready,
            Paused = false,
            PauseReason = null,
            CredentialId = credId,
            Mode = mode,
            FillStrategy = SelectedStrategy,
            AllowInsecureHttp = allowHttp,
            AllowedOrigin = parsedOrigin,
            AllowedLoginPath = _loginPathBox.Text,
            AllowedSubmitPath = _submitPathBox.Text,
            UsernameSelector = mode == PortalAuthMode.CredentialsForm ? _userSelectorBox.Text : null,
            PasswordSelector = mode == PortalAuthMode.CredentialsForm ? _passSelectorBox.Text : null,
            SubmitSelector = _submitSelectorBox.Text,
            FixedSelectValues = selects
        };
        built.Normalize();

        if (!built.IsConfigurationValid())
        {
            ShowBanner(_localizer.T("PortalValidationFieldsRequired"), isError: true);
            return false;
        }

        config = built;
        return true;
    }

    private async Task TestAndSaveAsync()
    {
        if (_testHandler is null || _isBusy) return;
        if (!TryBuildInput(ready: false, out var draft, out var credential) || draft is null) return;

        SetBusy(true, _localizer.T("PortalTestingStatus"), isError: false);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var result = await _testHandler(draft, credential, _discoveredEntryUri, cts.Token);
            if (!result.Success)
            {
                ShowBanner(string.Format(_localizer.T("PortalTestFailed"), result.Message), isError: true);
                return;
            }

            ResultConfig = result.VerifiedConfig ?? draft;
            ResultConfig.Ready = true;
            ResultConfig.Paused = false;
            ResultConfig.PauseReason = null;
            ResultCredential = credential;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowBanner(string.Format(_localizer.T("PortalTestFailed"), ex.Message), isError: true);
        }
        finally
        {
            SetBusy(false, null, isError: false);
        }
    }

    private void SavePending()
    {
        if (_isBusy) return;
        if (!TryBuildInput(ready: false, out var draft, out var credential) || draft is null) return;
        ResultConfig = draft;
        ResultCredential = credential;
        DialogResult = true;
    }

    private void ClearSetup()
    {
        if (_isBusy) return;
        ClearRequested = true;
        ResultConfig = null;
        ResultCredential = null;
        DialogResult = true;
    }

    private void SetBusy(bool busy, string? statusMessage, bool isError)
    {
        _isBusy = busy;
        _detectButton.IsEnabled = !busy && _detectHandler is not null;
        _testButton.IsEnabled = !busy && _testHandler is not null;
        _savePendingButton.IsEnabled = !busy;
        _clearButton.IsEnabled = !busy;
        _strategyBox.IsEnabled = !busy && SelectedMode == PortalAuthMode.CredentialsForm;
        if (!string.IsNullOrWhiteSpace(statusMessage)) ShowBanner(statusMessage, isError);
    }

    private void ShowBanner(string message, bool isError)
    {
        _feedbackText.Text = message;
        _feedbackText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "StatusDangerBrush" : "StatusInfoBrush");
        _feedbackText.Visibility = Visibility.Visible;
    }

    private static void AddField(Panel parent, string label, Control control)
    {
        var panel = new StackPanel();
        panel.SetResourceReference(MarginProperty, "SpacingFieldGap");
        var caption = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold };
        caption.SetResourceReference(MarginProperty, "SpacingFieldCaptionGap");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(caption);
        AutomationProperties.SetLabeledBy(control, caption);
        panel.Children.Add(control);
        parent.Children.Add(panel);
    }
}
