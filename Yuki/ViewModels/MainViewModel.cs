using CommunityToolkit.Mvvm.ComponentModel;

namespace Yuki.ViewModels;

public partial class MainViewModel : ObservableObject {
    [ObservableProperty]
    private bool isSpeaking;

    [ObservableProperty]
    private string state = "Готова";
}
