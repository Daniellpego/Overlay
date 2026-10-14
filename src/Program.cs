using System;
using System.Threading;
using System.Windows.Forms;

namespace Overlay
{
    static class Program
    {
        public const string ArgumentoReiniciando = "--reiniciando";
        // Usado pelo tools\instalar.ps1 (já elevado) para recriar a tarefa de logon apontando para a cópia instalada.
        public const string ArgumentoAtivarInicio = "--ativar-inicio";

        [STAThread]
        static int Main(string[] args)
        {
            if (Array.IndexOf(args, ArgumentoAtivarInicio) >= 0)
            {
                try
                {
                    return InicioAutomatico.Ativar(Application.ExecutablePath) ? 0 : 1;
                }
                catch (Exception ex)
                {
                    // Roda antes dos handlers globais: sem este catch o instalador veria a caixa de erro do .NET.
                    Log.Erro(ex);
                    return 1;
                }
            }

            Mutex mutex;
            bool primeiraInstancia;
            try
            {
                mutex = new Mutex(true, "Local\\OverlayHorarioPingFps", out primeiraInstancia);
            }
            catch (UnauthorizedAccessException)
            {
                // Já existe uma instância elevada (ex.: a do logon) e esta não é: o mutex dela nega acesso. Basta sair.
                return 0;
            }

            using (mutex)
            {
                // Ao reiniciar como administrador, a instância antiga ainda segura o mutex por alguns instantes.
                if (!primeiraInstancia && Array.IndexOf(args, ArgumentoReiniciando) >= 0)
                {
                    try { primeiraInstancia = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { primeiraInstancia = true; }
                }
                if (!primeiraInstancia) return 0;

                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Erro(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Erro(e.ExceptionObject as Exception);

                Application.Run(new OverlayForm(Config.Carregar()));
                mutex.ReleaseMutex();
            }
            return 0;
        }
    }
}
