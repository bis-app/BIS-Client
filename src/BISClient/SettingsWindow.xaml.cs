using System.Windows;
using BISClient.ViewModels;

namespace BISClient;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri("pack://application:,,,/Resources/App.ico", UriKind.Absolute));
        UploadTokenBox.Password = viewModel.UploadToken ?? string.Empty;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        viewModel.UploadToken = UploadTokenBox.Password;
        try
        {
            if (!viewModel.TrySave(out var error))
            {
                MessageBox.Show(
                    this,
                    error ?? "Die Server-Adresse ist ungültig.",
                    "BIS Client",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Die Einstellungen konnten nicht gespeichert werden.\n\n" + ex.Message,
                "BIS Client",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
