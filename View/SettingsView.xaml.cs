using STranslate.Plugin.Translate.OpenAICompetitive.ViewModel;
using System.Windows.Input;

namespace STranslate.Plugin.Translate.OpenAICompetitive.View;

public partial class SettingsView
{
    public SettingsView() => InitializeComponent();

    private void OnModelLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ModelBox.IsKeyboardFocusWithin && DataContext is SettingsViewModel viewModel)
            viewModel.AddModel(ModelBox.Text);
    }
}
