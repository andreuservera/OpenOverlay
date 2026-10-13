using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IRacingOverlay.App.ControlPanel;

public partial class PreviewBackdrop : UserControl
{
    /// <summary>The photo used where no widget picks its own: the layout previews and the editor.</summary>
    public const string DefaultPhoto = "spa2.jpg";

    /// <summary>Every circuit photo in Assets, for the previews that pick one per item.</summary>
    public static IReadOnlyList<string> AllPhotos { get; } =
        ["bcn.jpg", "nogaro.jpg", "singapur.jpg", "spa.jpg", "spa2.jpg"];

    /// <summary>A photo for a layout, the same one every time it is drawn, so its preview and its
    /// editor show the same circuit and different layouts mostly show different ones.</summary>
    public static string PhotoFor(Guid id) => AllPhotos[(int)((uint)id.GetHashCode() % (uint)AllPhotos.Count)];

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(PreviewBackdropMode), typeof(PreviewBackdrop),
        new PropertyMetadata(PreviewBackdropMode.Circuit, (d, _) => ((PreviewBackdrop)d).ApplyMode()));

    public static readonly DependencyProperty PhotoProperty = DependencyProperty.Register(
        nameof(Photo), typeof(string), typeof(PreviewBackdrop),
        new PropertyMetadata(DefaultPhoto, (d, _) => ((PreviewBackdrop)d).ApplyPhoto()));

    /// <summary>Decoded photos, colour and grayscale, by file name. Switching widgets switches photos,
    /// and decoding a JPEG every time the rail selection moves would be felt.</summary>
    private static readonly Dictionary<string, (ImageSource Color, ImageSource Gray)> Photos = [];

    public PreviewBackdrop()
    {
        InitializeComponent();
        ApplyPhoto();
    }

    /// <summary>What the widget is shown over: the circuit, a bright sky, or a checkerboard.</summary>
    public PreviewBackdropMode Mode
    {
        get => (PreviewBackdropMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>File name under Assets of the circuit photograph.</summary>
    public string Photo
    {
        get => (string)GetValue(PhotoProperty);
        set => SetValue(PhotoProperty, value);
    }

    private void ApplyMode()
    {
        Sky.Visibility = Mode == PreviewBackdropMode.Sky ? Visibility.Visible : Visibility.Collapsed;
        Checkerboard.Visibility = Mode == PreviewBackdropMode.Checkerboard ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyPhoto()
    {
        var name = string.IsNullOrEmpty(Photo) ? DefaultPhoto : Photo;
        if (!Photos.TryGetValue(name, out var photo))
        {
            var color = new BitmapImage();
            color.BeginInit();
            color.UriSource = new Uri($"pack://application:,,,/Assets/{name}");
            color.DecodePixelWidth = 1280;
            color.CacheOption = BitmapCacheOption.OnLoad;
            color.EndInit();
            color.Freeze();

            var gray = new FormatConvertedBitmap(color, PixelFormats.Gray8, null, 0);
            gray.Freeze();

            photo = (color, gray);
            Photos[name] = photo;
        }

        // spa2 is cropped to its horizon band (see the XAML); the others are framed on their subject.
        var viewbox = name == DefaultPhoto ? new Rect(0, 0.30, 1, 0.56) : new Rect(0, 0, 1, 1);
        GrayPhoto.ImageSource = photo.Gray;
        GrayPhoto.Viewbox = viewbox;
        ColorPhoto.ImageSource = photo.Color;
        ColorPhoto.Viewbox = viewbox;
    }
}
