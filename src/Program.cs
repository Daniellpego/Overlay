using System;
using System.IO;
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

    static class Log
    {
        static readonly object trava = new object();

        public static string Caminho
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay.log"); }
        }

        public static void Erro(Exception ex)
        {
            Mensagem(ex == null ? "Erro desconhecido" : ex.ToString());
        }

        public static void Mensagem(string texto)
        {
            try
            {
                lock (trava)
                    File.AppendAllText(Caminho, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + texto + Environment.NewLine);
            }
            catch (Exception)
            {
                // Sem log é melhor do que derrubar o overlay no meio do jogo.
            }
        }
    }
}
