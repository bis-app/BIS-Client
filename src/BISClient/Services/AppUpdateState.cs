using CommunityToolkit.Mvvm.ComponentModel;

namespace BISClient.Services;

public sealed partial class AppUpdateState : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(ShowApplyButton))]
    private bool _isAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(ShowApplyButton))]
    private bool _isDismissed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(ShowApplyButton))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(ShowApplyButton))]
    private AppUpdateInfo? _current;

    public bool IsBannerVisible => IsAvailable && !IsDismissed && Current is not null;

    public bool CanApply => IsBannerVisible && !IsBusy && Current is not null;

    public bool ShowApplyButton => IsBannerVisible && !IsBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string BannerText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                return ErrorMessage;
            }

            if (IsBusy)
            {
                return Progress > 0
                    ? $"Update wird heruntergeladen … {Progress:0} %"
                    : "Update wird heruntergeladen …";
            }

            var info = Current;
            if (info is null)
            {
                return string.Empty;
            }

            var text = $"Version {info.DisplayVersion} ist verfügbar.";
            if (!string.IsNullOrWhiteSpace(info.Notes))
            {
                text += " " + info.Notes.Trim();
            }

            return text;
        }
    }

    public void Offer(AppUpdateInfo info, bool forceShow = false)
    {
        if (forceShow || Current is null || ClientVersion.IsNewer(info.Version, Current.Version))
        {
            IsDismissed = false;
        }

        Current = info;
        ErrorMessage = null;
        Progress = 0;
        IsBusy = false;
        IsAvailable = true;
    }

    public void Remind()
    {
        if (Current is null || !IsAvailable || IsBusy)
        {
            return;
        }

        IsDismissed = false;
        ErrorMessage = null;
    }

    public void Dismiss()
    {
        if (IsBusy)
        {
            return;
        }

        IsDismissed = true;
        ErrorMessage = null;
    }

    public void BeginDownload()
    {
        ErrorMessage = null;
        Progress = 0;
        IsBusy = true;
    }

    public void ReportProgress(double percent)
    {
        var value = Math.Clamp(percent, 0, 100);
        UiThread.Invoke(() => Progress = value);
    }

    public void Fail(string message)
    {
        IsBusy = false;
        Progress = 0;
        ErrorMessage = message;
    }
}
