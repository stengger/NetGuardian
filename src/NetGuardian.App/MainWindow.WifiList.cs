using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App;

public partial class MainWindow
{
    private void NearbyFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_nearbyView is null) return;
        _nearbyView.Update(NearbySearchTextBox.Text, NearbyOnlyNewCheckBox.IsChecked == true,
            NearbySortComboBox.SelectedIndex == 1);
        UpdateEmptyStates();
    }

    private void ClearNearbyFilter_Click(object sender, RoutedEventArgs e)
    {
        NearbySearchTextBox.Clear();
        NearbyOnlyNewCheckBox.IsChecked = false;
    }
}
