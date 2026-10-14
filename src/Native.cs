using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Overlay
{
    static class Native
    {
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        const byte AC_SRC_OVER = 0x00;
        const byte AC_SRC_ALPHA = 0x01;
        const int ULW_ALPHA = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int Cx, Cy; public SIZE(int cx, int cy) { Cx = cx; Cy = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

        // Janela "layered" com alpha por pixel: fundo semitransparente e texto suavizado, sem a borda serrilhada do TransparencyKey.
        public static void DesenharJanela(IntPtr hwnd, Bitmap bmp, Point posicao)
        {
            IntPtr telaDC = GetDC(IntPtr.Zero);
            IntPtr memDC = CreateCompatibleDC(telaDC);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr anterior = SelectObject(memDC, hBitmap);
            try
            {
                var destino = new POINT(posicao.X, posicao.Y);
                var tamanho = new SIZE(bmp.Width, bmp.Height);
                var origem = new POINT(0, 0);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(hwnd, telaDC, ref destino, ref tamanho, memDC, ref origem, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDC, anterior);
                DeleteObject(hBitmap);
                DeleteDC(memDC);
                ReleaseDC(IntPtr.Zero, telaDC);
            }
        }

        public static void ManterNoTopo(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
