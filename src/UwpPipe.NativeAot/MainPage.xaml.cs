using Microsoft.UI.Xaml.Controls;
using UwpPipe.Common;
using UwpPipe.NativeAot.ViewModels;
using Windows.UI.Xaml.Controls;

namespace UwpPipe.NativeAot;

/// <summary>
/// An empty page that can be used on its own or navigated to within a <see cref="Frame">.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
    }

    private void OnPipeModeRadioButtonsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RadioButtons radioButtons = (RadioButtons)sender;
        ViewModel.SelectedPipeMode = radioButtons.SelectedItem switch
        {
            PipeMode mode => mode,
            _ => default
        };
    }
}
