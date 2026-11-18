using Autodesk.Revit.UI;

namespace RevitMCPCommandSet.Helpers
{
    public static class ConfirmationHelper
    {
        /// <summary>
        /// Shows a native Revit TaskDialog asking the user to confirm a destructive operation.
        /// Returns true if the user clicks Yes, false otherwise.
        /// </summary>
        public static bool Confirm(string action, int elementCount)
        {
            if (elementCount <= 0) return true;

            var dialog = new TaskDialog("MCP Operation Confirmation")
            {
                MainContent = $"About to {action} {elementCount} element(s). Continue?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };

            return dialog.Show() == TaskDialogResult.Yes;
        }
    }
}
