using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Windows;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var app = new Application();
        // El tema Fluent de Windows 11 por diccionario y no con la API experimental ThemeMode (WPF0001): el
        // efecto es el mismo (claro u oscuro según Windows, Mica de fondo, color de acento) sin depender
        // de una API que todavía puede cambiar.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
        });
        app.Run(new Ventana(args));
    }
}

// Comprueba la firma digital (Authenticode) de un ejecutable antes de correrlo como administrador.
static class Authenticode
{
    public static bool IsSignedBy(string path, string publisher)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = pathPtr }, fileInfo, false);
            var data = new WinTrustData { Size = (uint)Marshal.SizeOf<WinTrustData>(), UIChoice = 2, UnionChoice = 1, FileInfo = fileInfo }; // sin UI, archivo
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
#pragma warning disable SYSLIB0057 // X509CertificateLoader no lee el firmante de un .exe; esta es la vía que sigue funcionando
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0 &&
                   X509Certificate.CreateFromSignedFile(path).Subject.Contains(publisher, StringComparison.OrdinalIgnoreCase);
#pragma warning restore SYSLIB0057
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    [DllImport("wintrust.dll")]
    static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
    }
}
