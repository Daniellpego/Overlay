using System;
using System.Threading;
using System.Windows.Forms;

namespace Overlay
{
    static class Program
    {
        public const string ArgumentoReiniciando = "--reiniciando";

        [STAThread]
        static void Main(string[] args)
        {
            bool primeiraInstancia;
            using (var mutex = new Mutex(true, "Local\\OverlayHorarioPingFps", out primeiraInstancia))
            {
                // Ao reiniciar como administrador, a instância antiga ainda segura o mutex por alguns instantes.
                if (!primeiraInstancia && Array.IndexOf(args, ArgumentoReiniciando) >= 0)
                {
                    try { primeiraInstancia = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { primeiraInstancia = true; }
                }
                if (!primeiraInstancia) return;

                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Erro(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Erro(e.ExceptionObject as Exception);

                Application.Run(new OverlayForm(Config.Carregar()));
                mutex.ReleaseMutex();
            }
        }
    }
}
