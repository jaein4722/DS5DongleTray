using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DS5DongleTray;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--once", StringComparison.OrdinalIgnoreCase)))
        {
            ConsoleHelper.Attach();
            var client = new DongleHidClient();
            var snapshot = await client.ReadSnapshotAsync();
            ConsoleHelper.WriteLine(snapshot.ToConsoleText());
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext(new DongleHidClient()));
    }
}

internal static class ConsoleHelper
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static void Attach()
    {
        AttachConsole(AttachParentProcess);
    }

    public static void WriteLine(string text)
    {
        Console.WriteLine(text);
    }
}
