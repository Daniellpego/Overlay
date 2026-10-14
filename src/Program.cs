using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Overlay
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool primeiraInstancia;
            using (var mutex = new Mutex(true, "Local\\OverlayHorarioPingFps", out primeiraInstancia))
            {
                if (!primeiraInstancia) return;

                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Erro(e.Exception);

                Application.Run(new OverlayForm(Config.Carregar()));
            }
        }
    }

    static class Log
    {
        public static string Caminho
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay.log"); }
        }

        public static void Erro(Exception ex)
        {
            try
            {
                File.AppendAllText(Caminho, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine);
            }
            catch (Exception)
            {
                // Sem log é melhor do que derrubar o overlay no meio do jogo.
            }
        }
    }
}
