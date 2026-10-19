using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

internal sealed record NetworkOperationMessage(string Key, object[] Arguments, bool ReadFailed)
{
    public string Format(Localizer localizer)
    {
        var result = string.Format(localizer.T(Key), Arguments);
        return ReadFailed ? $"{result} {localizer.T("NetworkReadFailed")}" : result;
    }
}
