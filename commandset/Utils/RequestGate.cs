using System.Threading;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Tracks one request handed to Revit through an ExternalEvent.
    /// Revit only runs external events while it's idle, so a request can wait a long time to start.
    /// If the caller gives up before Revit starts it, the request is cancelled and never runs, so a call
    /// reported as timed out can't change the model later, and a retry can't apply the change twice.
    /// </summary>
    /// <remarks>Has no Revit dependencies so it can be tested outside Revit.</remarks>
    public sealed class RequestGate
    {
        private enum State
        {
            Idle,
            Pending,
            Running
        }

        public sealed class Run
        {
            internal readonly ManualResetEventSlim Completed = new ManualResetEventSlim(false);

            /// <summary>The exception the handler threw, if any.</summary>
            public Exception Error { get; internal set; }
        }

        private readonly object _sync = new object();
        private State _state = State.Idle;
        private Run _current;

        /// <summary>True while a request is waiting for Revit to start it or is running.</summary>
        public bool IsBusy
        {
            get
            {
                lock (_sync)
                {
                    return _state != State.Idle;
                }
            }
        }

        /// <summary>
        /// Registers a new request. Call before raising the event, so a fast handler can't finish before
        /// anyone is listening.
        /// </summary>
        /// <exception cref="InvalidOperationException">The previous request hasn't finished.</exception>
        public Run Begin()
        {
            lock (_sync)
            {
                if (_state != State.Idle)
                {
                    throw new InvalidOperationException(
                        "The previous request for this command is still waiting for Revit or running.");
                }

                _state = State.Pending;
                _current = new Run();
                return _current;
            }
        }

        /// <summary>
        /// Called on Revit's thread when the event fires. Returns the request to run, or null when there is
        /// none, e.g. a leftover event for a request that was cancelled.
        /// </summary>
        public Run TryStart()
        {
            lock (_sync)
            {
                if (_state != State.Pending)
                {
                    return null;
                }

                _state = State.Running;
                return _current;
            }
        }

        /// <summary>Called on Revit's thread when the handler returns or throws.</summary>
        public void Finish(Run run, Exception error)
        {
            run.Error = error;
            lock (_sync)
            {
                _state = State.Idle;
            }
            run.Completed.Set();
        }

        /// <summary>
        /// Waits up to <paramref name="startTimeoutMilliseconds"/> for Revit to start the request. Returns false
        /// if it didn't, after cancelling it. Once started, waits for it to finish, however long that takes.
        /// </summary>
        public bool Wait(Run run, int startTimeoutMilliseconds)
        {
            if (run.Completed.Wait(startTimeoutMilliseconds))
            {
                return true;
            }

            if (Cancel(run))
            {
                return false;
            }

            run.Completed.Wait();
            return true;
        }

        /// <summary>Cancels the request if Revit hasn't started it yet. Returns true if it was cancelled.</summary>
        public bool Cancel(Run run)
        {
            lock (_sync)
            {
                if (_current != run || _state != State.Pending)
                {
                    return false;
                }

                _state = State.Idle;
                return true;
            }
        }
    }
}
