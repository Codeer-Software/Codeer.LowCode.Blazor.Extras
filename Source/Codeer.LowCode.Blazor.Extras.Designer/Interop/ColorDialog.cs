using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Codeer.LowCode.Blazor.Extras.Designer.Interop
{
    internal class ColorDialog : Microsoft.Win32.CommonDialog
    {
        private const int SavedColors = 16;
        private static int[] _colors = new int[SavedColors];
        public ColorFlags Flags { get; set; } = ColorFlags.AnyColor | ColorFlags.FullOpen | ColorFlags.RgbInit;
        public Color Color { get; set; }
        public Color[] CustomColors { get; set; } = _colors.Select(ColorRefToColor).ToArray();

        public override void Reset()
        {
            Color = Colors.Black;
            CustomColors = _colors.Select(ColorRefToColor).ToArray();
        }

        protected override bool RunDialog(IntPtr hwndOwner)
        {
            var custColors = CustomColors.Select(ColorToColorRef).Concat(new int[SavedColors]).Take(SavedColors)
                .ToArray();
            using var lpCustColors = new CoTaskMem<int>(SavedColors);
            Marshal.Copy(custColors, 0, lpCustColors, SavedColors);

            var cc = new CHOOSECOLOR
            {
                hwndOwner = hwndOwner,
                rgbResult = ColorToColorRef(Color),
                lpCustColors = lpCustColors,
                Flags = Flags,
            };
            var result = CommonDialog.ChooseColor(ref cc);
            if (result)
            {
                Color = ColorRefToColor(cc.rgbResult);
                Marshal.Copy(lpCustColors, custColors, 0, SavedColors);
                _colors = custColors.ToArray();
                CustomColors = _colors.Select(ColorRefToColor).ToArray();
            }

            return result;
        }

        private static int ColorToColorRef(Color color)
        {
            var colorRef = 0;
            colorRef |= color.R;
            colorRef |= color.G << 8;
            colorRef |= color.B << 16;
            return colorRef;
        }

        private static Color ColorRefToColor(int colorRef)
        {
            var color = new Color
            {
                R = (byte)(colorRef & 0xFF),
                G = (byte)((colorRef >> 8) & 0xFF),
                B = (byte)((colorRef >> 16) & 0xFF)
            };
            return color;
        }
    }
}
