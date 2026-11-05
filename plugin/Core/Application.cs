using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;



namespace revit_mcp_plugin.Core
{
    public class Application : IExternalApplication
    {
        // Referentie naar de toggle-knop zodat MCPServiceConnection het icoon kan bijwerken
        // nadat de server is gestart/gestopt.
        private static PushButton _toggleButton;

        public Result OnStartup(UIControlledApplication application)
        {
            // aanmaken ribbontab niet nodig. bestaat al vanuit modulefabrique addin
            string myRibbon = "ModuleFabrique";
            RibbonPanel mcpPanel;
            try
            {
                mcpPanel = application.CreateRibbonPanel(myRibbon,"MCP");
            }
            catch (Exception)
            {
                throw new Exception("Ribbonpanel 'MCP' kan niet worden aangemaakt. is modulefabrique addin al geladen?");
            }
            
            PushButtonData pushButtonData = new PushButtonData("ID_EXCMD_TOGGLE_REVIT_MCP", "Revit MCP\r\n Switch",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.MCPServiceConnection");
            pushButtonData.ToolTip = "Open / Close mcp server";

            _toggleButton = mcpPanel.AddItem(pushButtonData) as PushButton;

            // Toon direct de juiste icoon-status: de server start niet automatisch op, dus dicht (zwart).
            UpdateToggleButtonIcon(isRunning: false);

            //PushButtonData mcp_settings_pushButtonData = new PushButtonData("ID_EXCMD_MCP_SETTINGS", "Settings",
            //    Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.Settings");
            //mcp_settings_pushButtonData.ToolTip = "MCP Settings";
            //mcp_settings_pushButtonData.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/settings-16.png", UriKind.RelativeOrAbsolute));
            //mcp_settings_pushButtonData.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/settings-32.png", UriKind.RelativeOrAbsolute));
            //mcpPanel.AddItem(mcp_settings_pushButtonData);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                if (SocketService.Instance.IsRunning)
                {
                    SocketService.Instance.Stop();
                }
            }
            catch { }

            return Result.Succeeded;
        }

        /// <summary>
        /// Werkt het icoon van de toggle-knop bij zodat visueel zichtbaar is of de mcp-server
        /// open (orange) of dicht (black) is. Wordt aangeroepen vanuit MCPServiceConnection nadat
        /// de server is gestart/gestopt, en één keer bij het opstarten van de add-in.
        /// </summary>
        internal static void UpdateToggleButtonIcon(bool isRunning)
        {
            if (_toggleButton == null)
                return;

            string color = isRunning ? "orange" : "black";

            _toggleButton.Image = LoadIcon($"mcp-server-{color}-16.png");
            _toggleButton.LargeImage = LoadIcon($"mcp-server-{color}-32.png");
        }

        /// <summary>
        /// Laadt een icoon uit de embedded resources (Core/Resources, build action EmbeddedResource).
        /// Geen pack-URI's gebruiken: die vinden alleen bestanden met build action 'Resource'.
        /// </summary>
        private static BitmapSource LoadIcon(string fileName)
        {
            string resourceName = $"revit_mcp_plugin.Core.Resources.{fileName}";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException($"Icoon '{resourceName}' niet gevonden in de embedded resources.");

                // OnLoad: de afbeelding direct volledig inlezen, zodat de stream daarna gesloten kan worden.
                var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
        }
    }
}
