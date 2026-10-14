using System;
using System.IO;

namespace Overlay
{
    static class Log
    {
        static readonly object trava = new object();

        public static string Caminho
        {
            get { return Path.Combine(Pastas.Log, "overlay.log"); }
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
