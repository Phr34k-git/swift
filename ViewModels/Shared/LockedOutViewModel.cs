using System;
using Client.Services;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IImage = Avalonia.Media.IImage;

namespace Client.ViewModels;

public sealed class LockedOutViewModel : ViewModelBase
{
    private const string DefaultImageSource = "avares://Client/Assets/locked-out.png";
    private const string MaintenanceImageSource = "avares://Client/Assets/maintenance.png";

    private string _title = LockedOutCopy.Resolve(null).Title;
    private string _subtext = LockedOutCopy.Resolve(null).Subtext;
    private string _imageAssetUri = DefaultImageSource;
    private IImage? _imageSource = LoadImage(DefaultImageSource);
    private string? _reason;

    /// <summary>
    /// Gets or sets the server-side lockout reason code.
    /// Setting this updates <see cref="Title"/> and <see cref="Subtext"/> from the copy map.
    /// </summary>
    public string? Reason
    {
        get => _reason;
        set
        {
            if (!SetProperty(ref _reason, value))
            {
                return;
            }

            var entry = LockedOutCopy.Resolve(value);
            var imageAssetUri = value == "maintenance" ? MaintenanceImageSource : DefaultImageSource;
            Title = entry.Title;
            Subtext = entry.Subtext;
            ImageAssetUri = imageAssetUri;
            ImageSource = LoadImage(imageAssetUri);
        }
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtext
    {
        get => _subtext;
        private set => SetProperty(ref _subtext, value);
    }

    public string ImageAssetUri
    {
        get => _imageAssetUri;
        private set => SetProperty(ref _imageAssetUri, value);
    }

    public IImage? ImageSource
    {
        get => _imageSource;
        private set => SetProperty(ref _imageSource, value);
    }

    private static Bitmap? LoadImage(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return new Bitmap(stream);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
