using System.Runtime.InteropServices;

namespace Codeer.LowCode.Blazor.Extras.Designer.Interop
{
    internal class CommonDialog
    {
        [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ChooseColor([In, Out] ref CHOOSECOLOR lpcc);

        [DllImport("comdlg32.dll")]
        internal static extern uint CommDlgExtendedError();
    }

    internal class CoTaskMem<T> : IDisposable
        where T : struct
    {
        private readonly IntPtr _mem;
        private bool _disposed = false;

        public CoTaskMem(int elements)
        {
            _mem = Marshal.AllocCoTaskMem(Marshal.SizeOf<T>() * elements);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Marshal.FreeCoTaskMem(_mem);
            GC.SuppressFinalize(this);
        }

        public static implicit operator nint(CoTaskMem<T> mem) => mem._mem;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct CHOOSECOLOR
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public int rgbResult;
        public IntPtr lpCustColors;
        public ColorFlags Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;

        public CHOOSECOLOR()
        {
            lStructSize = (uint)Marshal.SizeOf(typeof(CHOOSECOLOR));
        }
    }

    [Flags]
    internal enum ColorFlags : int
    {
        AnyColor = 0x00000100,
        EnableHook = 0x00000010,
        EnableTemplate = 0x00000020,
        EnableTemplateHandle = 0x00000040,
        FullOpen = 0x00000002,
        PreventFullOpen = 0x00000004,
        RgbInit = 0x00000001,
        ShowHelp = 0x00000008,
        SolidColor = 0x00000080
    }
}
