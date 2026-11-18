using System.Windows;
using System.Windows.Controls;

namespace revit_mcp_plugin.UI
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private GeneralSettingsPage generalPage;
        private ApiKeySettingsPage apiKeyPage;
        private CommandSetSettingsPage commandSetPage;
        private bool isInitialized = false;

        public SettingsWindow()
        {
            InitializeComponent();

            // Initialize pages
            generalPage = new GeneralSettingsPage();
            apiKeyPage = new ApiKeySettingsPage();
            commandSetPage = new CommandSetSettingsPage();

            // Load default page (General)
            ContentFrame.Navigate(generalPage);

            isInitialized = true;
        }

        private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;

            if (NavListBox.SelectedItem == GeneralItem)
            {
                ContentFrame.Navigate(generalPage);
            }
            else if (NavListBox.SelectedItem == ApiKeyItem)
            {
                ContentFrame.Navigate(apiKeyPage);
            }
            else if (NavListBox.SelectedItem == CommandSetItem)
            {
                ContentFrame.Navigate(commandSetPage);
            }
        }
    }
}
