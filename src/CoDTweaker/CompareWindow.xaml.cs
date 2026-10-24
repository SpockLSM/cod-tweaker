using System.Windows;
using CoDTweaker.ViewModels;
using Microsoft.Win32;
using Spock.AppKit;

namespace CoDTweaker;

public partial class CompareWindow : Window
{
    public CompareWindow(CompareViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);
        vm.PickFile ??= () =>
        {
            var dlg = new OpenFileDialog { Title = "Compare with a settings file", Filter = "Settings files (*.txt)|*.txt|All files|*.*" };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };
        vm.PickPreset ??= () =>
        {
            var dlg = new OpenFileDialog { Title = "Compare with a preset", Filter = "CoD Tweaker presets (*.json)|*.json|All files|*.*" };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };
        vm.Alert ??= (title, text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        DataContext = vm;
    }
}
