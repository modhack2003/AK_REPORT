using System;
using System.Threading.Tasks;

namespace AkReporting.Desktop
{
    internal sealed class WorkspaceNavigation
    {
        private int generation;
        public void Invalidate() => generation++;

        // Fetch and validate the complete destination before asking to discard input.
        // The caller replaces editor state only after both steps have succeeded.
        public async Task<bool> TryReplace<T>(Func<Task<T>> fetch, Func<bool> confirmReplacement,
            Action<T> replace, Action restoreSelection)
        {
            var startedIn = generation;
            try
            {
                var destination = await fetch();
                if (startedIn != generation) return false;
                if (!confirmReplacement())
                {
                    if (startedIn == generation) restoreSelection();
                    return false;
                }
                // A modal discard prompt can process a pending session-expiry callback.
                if (startedIn != generation) return false;
                replace(destination);
                return true;
            }
            catch
            {
                if (startedIn == generation) restoreSelection();
                throw;
            }
        }
    }
}
