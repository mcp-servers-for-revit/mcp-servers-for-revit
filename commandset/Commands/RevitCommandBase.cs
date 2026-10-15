using System.Runtime.ExceptionServices;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Base;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Commands
{
    /// <summary>
    /// Base for commands that run their handler on Revit's thread through an ExternalEvent.
    /// It replaces the SDK's <c>RaiseAndWaitForCompletion</c>, which:
    /// <list type="bullet">
    /// <item>raises the event before the handler resets its completion signal, so a handler that finishes
    /// quickly can have its signal wiped and the call reports a timeout although it succeeded;</item>
    /// <item>leaves a timed-out request queued, so Revit still runs it later after the AI was told it failed,
    /// and a retry applies the change twice.</item>
    /// </list>
    /// Commands keep calling <c>Handler</c> and <c>RaiseAndWaitForCompletion</c> as before; both names are
    /// redeclared here so they bind to these versions.
    /// </summary>
    public abstract class RevitCommandBase : ExternalEventCommandBase
    {
        private readonly GatedEventHandler _gatedHandler;

        protected RevitCommandBase(IWaitableExternalEventHandler handler, UIApplication uiApp)
            : this(new GatedEventHandler(handler), uiApp)
        {
        }

        private RevitCommandBase(GatedEventHandler gatedHandler, UIApplication uiApp)
            : base(gatedHandler, uiApp)
        {
            _gatedHandler = gatedHandler;
        }

        /// <summary>
        /// The command's own handler, for setting its inputs and reading its results. Throws while an earlier
        /// request is still waiting or running, so another client can't overwrite that request's inputs.
        /// </summary>
        protected new IWaitableExternalEventHandler Handler
        {
            get
            {
                if (_gatedHandler.Gate.IsBusy)
                {
                    throw new InvalidOperationException(
                        $"Revit is still working on the previous '{CommandName}' request. Wait for it to finish, then try again.");
                }

                return _gatedHandler.Inner;
            }
        }

        /// <summary>
        /// Runs the handler on Revit's thread. <paramref name="timeoutMilliseconds"/> limits how long to wait for
        /// Revit to start it; once started it runs to completion. Rethrows anything the handler threw.
        /// </summary>
        /// <returns>Always true; failures throw. Kept as bool so existing commands compile unchanged.</returns>
        protected new bool RaiseAndWaitForCompletion(int timeoutMilliseconds = 10000)
        {
            RequestGate.Run run = _gatedHandler.Gate.Begin();
            Event.Raise();

            if (!_gatedHandler.Gate.Wait(run, timeoutMilliseconds))
            {
                throw new TimeoutException(
                    $"Revit didn't start '{CommandName}' within {timeoutMilliseconds / 1000.0:0.#} s, so it was cancelled and nothing was changed. " +
                    "Revit only runs add-in requests while it's idle: close any open dialog or finish the current command, then try again.");
            }

            if (run.Error != null)
            {
                ExceptionDispatchInfo.Capture(run.Error).Throw();
            }

            return true;
        }

        private sealed class GatedEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
        {
            public GatedEventHandler(IWaitableExternalEventHandler inner)
            {
                Inner = inner;
            }

            public IWaitableExternalEventHandler Inner { get; }

            public RequestGate Gate { get; } = new RequestGate();

            public void Execute(UIApplication app)
            {
                // Null for a leftover event whose request already timed out and was cancelled.
                RequestGate.Run run = Gate.TryStart();
                if (run == null)
                {
                    return;
                }

                Exception error = null;
                try
                {
                    ((IExternalEventHandler)Inner).Execute(app);
                }
                catch (Exception ex)
                {
                    // Keep handler exceptions out of Revit; RaiseAndWaitForCompletion rethrows them to the caller.
                    error = ex;
                }
                finally
                {
                    Gate.Finish(run, error);
                }
            }

            public string GetName() => ((IExternalEventHandler)Inner).GetName();

            // Only the SDK's RaiseAndWaitForCompletion calls this, and RevitCommandBase replaces that method.
            public bool WaitForCompletion(int timeoutMilliseconds) =>
                throw new NotSupportedException("Use RevitCommandBase.RaiseAndWaitForCompletion.");
        }
    }
}
