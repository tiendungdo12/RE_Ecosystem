using System.Threading;

namespace RE.WinUI.Views;

internal static class UsageDialogGate
{
    private static int _isOpen;

    public static bool TryEnter()
    {
        return Interlocked.CompareExchange(ref _isOpen, 1, 0) == 0;
    }

    public static void Exit()
    {
        Volatile.Write(ref _isOpen, 0);
    }
}