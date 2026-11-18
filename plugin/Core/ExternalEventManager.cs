using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Manages the creation and lifecycle of external events.
    /// </summary>
    public class ExternalEventManager
    {
        private static volatile ExternalEventManager _instance;
        private static readonly object _lock = new object();
        private Dictionary<string, ExternalEventWrapper> _events = new Dictionary<string, ExternalEventWrapper>();
        private bool _isInitialized = false;
        private UIApplication _uiApp;
        private ILogger _logger;

        /// <summary>
        /// Manages the creation and lifecycle of external events.
        /// </summary>
        public static ExternalEventManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new ExternalEventManager();
                    }
                }
                return _instance;
            }
        }

        private ExternalEventManager() { }

        public void Initialize(UIApplication uiApp, ILogger logger)
        {
            _uiApp = uiApp;
            _logger = logger;
            _isInitialized = true;
        }

        /// <summary>
        /// Obtain or create external events.
        /// </summary>
        public ExternalEvent GetOrCreateEvent(IWaitableExternalEventHandler handler, string key)
        {
            if (!_isInitialized)
                throw new InvalidOperationException($"{nameof(ExternalEventManager)} has not been initialized.");

            // If it exists and the processor matches, return directly.
            if (_events.TryGetValue(key, out var wrapper) &&
                wrapper.Handler == handler)
            {
                return wrapper.Event;
            }

            // You need to create events in the UI thread.
            ExternalEvent externalEvent = null;

            if (_uiApp.ActiveUIDocument == null)
                throw new InvalidOperationException("No active document is open in Revit. Open a document first.");

            // Perform the operation that created the event using the context of the active document.
            _uiApp.ActiveUIDocument.Document.Application.ExecuteCommand(
                (uiApp) => {
                    externalEvent = ExternalEvent.Create(handler);
                }
            );

            if (externalEvent == null)
                throw new InvalidOperationException("Unable to create external events.");

            // Store events.
            _events[key] = new ExternalEventWrapper
            {
                Event = externalEvent,
                Handler = handler
            };

            _logger.Info($"Created a new external event for key {key}.");

            return externalEvent;
        }

        /// <summary>
        /// Clears the event cache.
        /// </summary>
        public void ClearEvents()
        {
            _events.Clear();
        }

        private class ExternalEventWrapper
        {
            public ExternalEvent Event { get; set; }
            public IWaitableExternalEventHandler Handler { get; set; }
        }
    }
}

namespace Autodesk.Revit.DB
{
    public static class ApplicationExtensions
    {
        public delegate void CommandDelegate(UIApplication uiApp);

        /// <summary>
        /// Execute commands in the Revit context.
        /// </summary>
        public static void ExecuteCommand(this Autodesk.Revit.ApplicationServices.Application app, CommandDelegate command)
        {
            // This method is called in the Revit context and can safely create an ExternalEvent.
            command?.Invoke(new UIApplication(app));
        }
    }
}